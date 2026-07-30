//--------------------------//
//--------验证浏览器与终端 broker 的双向有界转发---------//
//--------Verifies bounded bidirectional relay between browsers and the terminal broker--------//
//-------------------------//
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nas.Api.Terminal;
using Nas.Application.Terminal;

namespace Nas.Api.Tests;

public sealed class TerminalWebSocketRelayTests
{
    [Fact]
    public async Task BrowserInputAndResizeAreForwardedToTheBroker()
    {
        using var webSocket = new FakeWebSocket(
            new WebSocketFrame(WebSocketMessageType.Binary, "whoami\r\n"u8.ToArray()),
            new WebSocketFrame(
                WebSocketMessageType.Text,
                """{"type":"resize","columns":100,"rows":28}"""u8.ToArray()),
            new WebSocketFrame(
                WebSocketMessageType.Text,
                """{"type":"close"}"""u8.ToArray()));
        var broker = new FakeTerminalBrokerSession { HoldEventsOpen = true };
        var relay = CreateRelay();

        await relay.RelayAsync(
            webSocket,
            broker,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("whoami\r\n", Encoding.UTF8.GetString(Assert.Single(broker.Input)));
        Assert.Equal(((ushort)100, (ushort)28), Assert.Single(broker.Resizes));
        Assert.Equal(1, broker.CloseCount);
        Assert.Equal(WebSocketState.Closed, webSocket.State);
    }

    [Fact]
    public async Task BrokerOutputAndExitAreForwardedToTheBrowser()
    {
        using var webSocket = new FakeWebSocket();
        var broker = new FakeTerminalBrokerSession();
        broker.Events.Add(new TerminalOutput("terminal-output"u8.ToArray()));
        broker.Events.Add(new TerminalExited(0));
        var relay = CreateRelay();

        await relay.RelayAsync(
            webSocket,
            broker,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, webSocket.SentFrames.Count);
        Assert.Equal(WebSocketMessageType.Binary, webSocket.SentFrames[0].MessageType);
        Assert.Equal(
            "terminal-output",
            Encoding.UTF8.GetString(webSocket.SentFrames[0].Payload));
        Assert.Equal(WebSocketMessageType.Text, webSocket.SentFrames[1].MessageType);
        using var exitDocument = JsonDocument.Parse(webSocket.SentFrames[1].Payload);
        Assert.Equal("exited", exitDocument.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, exitDocument.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, broker.CloseCount);
        Assert.Equal(WebSocketState.Closed, webSocket.State);
    }

    private static TerminalWebSocketRelay CreateRelay()
    {
        return new TerminalWebSocketRelay(
            Options.Create(new TerminalOptions
            {
                IdleTimeoutMinutes = 15,
                MaximumSessionMinutes = 60
            }),
            NullLogger<TerminalWebSocketRelay>.Instance);
    }

    private sealed record WebSocketFrame(
        WebSocketMessageType MessageType,
        byte[] Payload);

    private sealed class FakeWebSocket(params WebSocketFrame[] frames) : WebSocket
    {
        private readonly Queue<WebSocketFrame> receiveFrames = new(frames);
        private WebSocketCloseStatus? closeStatus;
        private string? closeStatusDescription;
        private WebSocketState state = WebSocketState.Open;

        public List<WebSocketFrame> SentFrames { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => closeStatus;
        public override string? CloseStatusDescription => closeStatusDescription;
        public override WebSocketState State => state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            this.closeStatus = closeStatus;
            closeStatusDescription = statusDescription;
            state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            this.closeStatus = closeStatus;
            closeStatusDescription = statusDescription;
            state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            state = WebSocketState.Closed;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (receiveFrames.TryDequeue(out var frame))
            {
                frame.Payload.CopyTo(buffer.Array!, buffer.Offset);
                return Task.FromResult(new WebSocketReceiveResult(
                    frame.Payload.Length,
                    frame.MessageType,
                    endOfMessage: true));
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (receiveFrames.TryDequeue(out var frame))
            {
                frame.Payload.AsMemory().CopyTo(buffer);
                return ValueTask.FromResult(new ValueWebSocketReceiveResult(
                    frame.Payload.Length,
                    frame.MessageType,
                    endOfMessage: true));
            }

            return new ValueTask<ValueWebSocketReceiveResult>(
                WaitForValueCancellationAsync(cancellationToken));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentFrames.Add(new WebSocketFrame(messageType, buffer.AsSpan().ToArray()));
            return Task.CompletedTask;
        }

        public override ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentFrames.Add(new WebSocketFrame(messageType, buffer.ToArray()));
            return ValueTask.CompletedTask;
        }

        private static async Task<WebSocketReceiveResult> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation did not stop the receive");
        }

        private static async Task<ValueWebSocketReceiveResult> WaitForValueCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation did not stop the receive");
        }
    }

    private sealed class FakeTerminalBrokerSession : ITerminalBrokerSession
    {
        public List<byte[]> Input { get; } = [];
        public List<(ushort Columns, ushort Rows)> Resizes { get; } = [];
        public List<TerminalBrokerEvent> Events { get; } = [];
        public bool HoldEventsOpen { get; init; }
        public int CloseCount { get; private set; }

        public async IAsyncEnumerable<TerminalBrokerEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var brokerEvent in Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return brokerEvent;
                await Task.Yield();
            }

            if (HoldEventsOpen)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public Task SendInputAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken)
        {
            Input.Add(data.ToArray());
            return Task.CompletedTask;
        }

        public Task ResizeAsync(
            ushort columns,
            ushort rows,
            CancellationToken cancellationToken)
        {
            Resizes.Add((columns, rows));
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
