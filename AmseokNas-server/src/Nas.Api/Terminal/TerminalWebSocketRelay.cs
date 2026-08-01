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
    TimeProvider timeProvider,
    ILogger<TerminalWebSocketRelay> logger) : ITerminalWebSocketRelay
{
    private const int MaximumInputMessageBytes = 64 * 1024;
    private const int MaximumControlMessageBytes = 4 * 1024;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    public async Task RelayAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        using var absoluteTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.MaximumSessionMinutes),
            timeProvider);
        using var idleTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes),
            timeProvider);
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

        var completedTask = await Task.WhenAny(inputTask, outputTask);
        var completion = await ClassifyCompletionAsync(
            completedTask,
            cancellationToken,
            absoluteTimeout,
            idleTimeout);
        relayCancellation.Cancel();

        using var shutdownTimeout = new CancellationTokenSource(
            ShutdownTimeout,
            timeProvider);
        using var shutdownCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            shutdownTimeout.Token);
        var observeRelayTasks = ObserveRelayTasksAsync(inputTask, outputTask);

        await CloseBrokerAsync(
            brokerSession,
            sessionId,
            shutdownCancellation.Token);
        try
        {
            await observeRelayTasks.WaitAsync(shutdownCancellation.Token);
        }
        catch (OperationCanceledException)
            when (shutdownCancellation.IsCancellationRequested)
        {
            logger.LogWarning(
                "Terminal session {SessionId} relay tasks did not finish before shutdown cancellation",
                sessionId);
        }

        await CloseWebSocketAsync(
            webSocket,
            completion,
            sessionId,
            shutdownCancellation.Token);

        if (completion.Failure is not null)
        {
            logger.LogWarning(
                completion.Failure,
                "Terminal session {SessionId} relay ended because {Outcome}",
                sessionId,
                completion.Outcome);
        }

        logger.LogInformation(
            "Terminal session {SessionId} ended with {Outcome}; input {InputBytes} bytes, output {OutputBytes} bytes",
            sessionId,
            completion.Outcome,
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
                        throw new TerminalWebSocketProtocolException(
                            WebSocketCloseStatus.MessageTooBig,
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

                TerminalClientControlMessage? control;
                try
                {
                    control = JsonSerializer.Deserialize(
                        message.ToArray(),
                        TerminalApiJsonContext.Default.TerminalClientControlMessage);
                }
                catch (JsonException exception)
                {
                    throw new TerminalWebSocketProtocolException(
                        WebSocketCloseStatus.InvalidPayloadData,
                        "Terminal control message contains invalid JSON",
                        exception);
                }
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
                        throw new TerminalWebSocketProtocolException(
                            WebSocketCloseStatus.PolicyViolation,
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

    private static async Task<RelayCompletion> ClassifyCompletionAsync(
        Task completedTask,
        CancellationToken requestCancellationToken,
        CancellationTokenSource absoluteTimeout,
        CancellationTokenSource idleTimeout)
    {
        try
        {
            await completedTask;
        }
        catch (OperationCanceledException)
            when (requestCancellationToken.IsCancellationRequested
                || absoluteTimeout.IsCancellationRequested
                || idleTimeout.IsCancellationRequested)
        {
        }
        catch (TerminalWebSocketProtocolException exception)
        {
            return new RelayCompletion(
                "ProtocolViolation",
                exception.CloseStatus,
                exception.Message,
                exception);
        }
        catch (WebSocketException)
        {
            return new RelayCompletion(
                "Disconnected",
                WebSocketCloseStatus.NormalClosure,
                "Terminal client disconnected");
        }
        catch (Exception exception)
        {
            return new RelayCompletion(
                "Failed",
                WebSocketCloseStatus.InternalServerError,
                "Terminal transport failed",
                exception);
        }

        return absoluteTimeout.IsCancellationRequested
            ? new RelayCompletion(
                "MaximumDuration",
                WebSocketCloseStatus.NormalClosure,
                "Terminal maximum duration reached")
            : idleTimeout.IsCancellationRequested
                ? new RelayCompletion(
                    "IdleTimeout",
                    WebSocketCloseStatus.NormalClosure,
                    "Terminal idle timeout reached")
                : requestCancellationToken.IsCancellationRequested
                    ? new RelayCompletion(
                        "RequestCancelled",
                        WebSocketCloseStatus.EndpointUnavailable,
                        "Terminal request ended")
                    : new RelayCompletion(
                        "Closed",
                        WebSocketCloseStatus.NormalClosure,
                        "Terminal session ended");
    }

    private async Task CloseBrokerAsync(
        ITerminalBrokerSession brokerSession,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await brokerSession.CloseAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Terminal session {SessionId} broker close was cancelled before completion",
                sessionId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Terminal session {SessionId} broker close failed",
                sessionId);
        }
    }

    private async Task CloseWebSocketAsync(
        WebSocket webSocket,
        RelayCompletion completion,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await webSocket.CloseAsync(
                completion.CloseStatus,
                completion.CloseDescription,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            webSocket.Abort();
            logger.LogWarning(
                "Terminal session {SessionId} WebSocket close was cancelled and aborted",
                sessionId);
        }
        catch (WebSocketException)
        {
            // The browser may already have disconnected without completing the handshake.
            webSocket.Abort();
        }
    }

    private static async Task ObserveRelayTasksAsync(params Task[] tasks)
    {
        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch (Exception)
            {
                // The first completion was classified; remaining failures follow relay cancellation.
            }
        }
    }

    private sealed record RelayCompletion(
        string Outcome,
        WebSocketCloseStatus CloseStatus,
        string CloseDescription,
        Exception? Failure = null);

    private sealed class TerminalWebSocketProtocolException(
        WebSocketCloseStatus closeStatus,
        string message,
        Exception? innerException = null) : Exception(message, innerException)
    {
        public WebSocketCloseStatus CloseStatus { get; } = closeStatus;
    }
}
