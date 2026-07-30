//--------------------------//
//--------隔离浏览器与终端 broker 的有界 WebSocket 转发---------//
//--------Isolates bounded WebSocket relaying between browsers and the terminal broker--------//
//-------------------------//
using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Api.Contracts;
using Nas.Application.Terminal;

namespace Nas.Api.Terminal;

public interface ITerminalWebSocketRelay
{
    Task RelayAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        Guid sessionId,
        CancellationToken cancellationToken);
}

public sealed class TerminalWebSocketRelay(
    IOptions<TerminalOptions> options,
    ILogger<TerminalWebSocketRelay> logger) : ITerminalWebSocketRelay
{
    private const int MaximumInputMessageBytes = 64 * 1024;
    private const int MaximumControlMessageBytes = 4 * 1024;

    public async Task RelayAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        using var absoluteTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.MaximumSessionMinutes));
        using var idleTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes));
        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            absoluteTimeout.Token,
            idleTimeout.Token);
        long inputBytes = 0;
        long outputBytes = 0;

        void ReportActivity() => idleTimeout.CancelAfter(
            TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes));

        var inputTask = RelayBrowserInputAsync(
            webSocket,
            brokerSession,
            ReportActivity,
            count => Interlocked.Add(ref inputBytes, count),
            relayCancellation.Token);
        var outputTask = RelayBrokerOutputAsync(
            brokerSession,
            webSocket,
            ReportActivity,
            count => Interlocked.Add(ref outputBytes, count),
            relayCancellation.Token);

        string outcome;
        try
        {
            await Task.WhenAny(inputTask, outputTask);
            relayCancellation.Cancel();
            await brokerSession.CloseAsync(CancellationToken.None);
            await ObserveRelayTaskAsync(inputTask);
            await ObserveRelayTaskAsync(outputTask);
            outcome = absoluteTimeout.IsCancellationRequested
                ? "MaximumDuration"
                : idleTimeout.IsCancellationRequested
                    ? "IdleTimeout"
                    : "Closed";
        }
        catch (Exception exception)
        {
            outcome = "Failed";
            logger.LogWarning(
                exception,
                "Terminal session {SessionId} relay failed",
                sessionId);
        }

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Terminal session ended",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The browser may already have disconnected.
            }
        }

        logger.LogInformation(
            "Terminal session {SessionId} ended with {Outcome}; input {InputBytes} bytes, output {OutputBytes} bytes",
            sessionId,
            outcome,
            inputBytes,
            outputBytes);
    }

    private static async Task RelayBrowserInputAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        Action reportActivity,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested
                && webSocket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(
                        buffer.AsMemory(),
                        cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    var limit = result.MessageType == WebSocketMessageType.Binary
                        ? MaximumInputMessageBytes
                        : MaximumControlMessageBytes;
                    if (message.Length + result.Count > limit)
                    {
                        throw new InvalidDataException(
                            "Terminal WebSocket message exceeds its limit");
                    }
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                reportActivity();
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var data = message.ToArray();
                    if (data.Length > 0)
                    {
                        await brokerSession.SendInputAsync(data, cancellationToken);
                        reportBytes(data.Length);
                    }
                    continue;
                }

                var control = JsonSerializer.Deserialize(
                    message.ToArray(),
                    TerminalApiJsonContext.Default.TerminalClientControlMessage);
                switch (control)
                {
                    case TerminalResizeMessage resize
                        when resize.Columns is >= 20 and <= 300
                            && resize.Rows is >= 5 and <= 120:
                        await brokerSession.ResizeAsync(
                            resize.Columns,
                            resize.Rows,
                            cancellationToken);
                        break;
                    case TerminalCloseMessage:
                        return;
                    default:
                        throw new InvalidDataException(
                            "Terminal control message is invalid");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task RelayBrokerOutputAsync(
        ITerminalBrokerSession brokerSession,
        WebSocket webSocket,
        Action reportActivity,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        await foreach (var brokerEvent in brokerSession.ReadEventsAsync(cancellationToken))
        {
            reportActivity();
            switch (brokerEvent)
            {
                case TerminalOutput output:
                    await webSocket.SendAsync(
                        output.Data,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken);
                    reportBytes(output.Data.Length);
                    break;
                case TerminalExited exited:
                    await SendControlMessageAsync(
                        webSocket,
                        new TerminalServerControlMessage("exited", exited.ExitCode),
                        cancellationToken);
                    return;
                case TerminalBrokerError error:
                    await SendControlMessageAsync(
                        webSocket,
                        new TerminalServerControlMessage(
                            "error",
                            Code: error.Code,
                            Message: error.Message),
                        cancellationToken);
                    return;
            }
        }
    }

    private static async Task SendControlMessageAsync(
        WebSocket webSocket,
        TerminalServerControlMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            TerminalApiJsonContext.Default.TerminalServerControlMessage);
        await webSocket.SendAsync(
            payload.AsMemory(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task ObserveRelayTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
    }
}
