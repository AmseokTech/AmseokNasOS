//--------------------------//
//--------通过有界强类型协议访问低权限终端 broker---------//
//--------Accesses the low-privilege terminal broker through a bounded typed protocol--------//
//-------------------------//
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Nas.Application.Terminal;

namespace Nas.Infrastructure.Terminal;

public sealed class UnixSocketTerminalBrokerClient(IOptions<TerminalOptions> options)
    : ITerminalBrokerClient
{
    public async Task<ITerminalBrokerSession> OpenAsync(
        Guid sessionId,
        ushort columns,
        ushort rows,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint(options.Value.SocketPath),
                cancellationToken);
            var session = new UnixSocketTerminalBrokerSession(socket);
            await session.OpenAsync(sessionId, columns, rows, cancellationToken);
            return session;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed partial class UnixSocketTerminalBrokerSession(Socket socket) : ITerminalBrokerSession
{
    private const byte ClientStdin = 0x01;
    private const byte ClientResize = 0x02;
    private const byte ClientClose = 0x03;
    private const byte ServerOpened = 0x10;
    private const byte ServerStdout = 0x11;
    private const byte ServerExited = 0x12;
    private const byte ServerError = 0x13;
    private const int MaximumOpenFrameBytes = 4 * 1024;
    private const int MaximumDataFrameBytes = 64 * 1024;

    private readonly NetworkStream stream = new(socket, ownsSocket: true);
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private bool closed;

    public async Task OpenAsync(
        Guid sessionId,
        ushort columns,
        ushort rows,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new OpenRequest(1, sessionId, "maintenance", columns, rows),
            TerminalJsonContext.Default.OpenRequest);
        if (payload.Length > MaximumOpenFrameBytes)
        {
            throw new InvalidOperationException("Terminal open request exceeds the protocol limit");
        }

        await WriteLengthPrefixedAsync(payload, cancellationToken);
        var frame = await ReadFrameAsync(cancellationToken);
        if (frame.Type != ServerOpened)
        {
            throw new IOException("Terminal broker did not acknowledge the session");
        }

        var response = JsonSerializer.Deserialize(
            frame.Payload.Span,
            TerminalJsonContext.Default.OpenedResponse)
            ?? throw new IOException("Terminal broker returned an invalid acknowledgement");
        if (response.ProtocolVersion != 1 || response.SessionId != sessionId)
        {
            throw new IOException("Terminal broker acknowledgement did not match the request");
        }
    }

    public async IAsyncEnumerable<TerminalBrokerEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!closed)
        {
            BrokerFrame frame;
            try
            {
                frame = await ReadFrameAsync(cancellationToken);
            }
            catch (EndOfStreamException)
            {
                yield break;
            }

            switch (frame.Type)
            {
                case ServerStdout:
                    yield return new TerminalOutput(frame.Payload);
                    break;
                case ServerExited:
                    var exit = JsonSerializer.Deserialize(
                        frame.Payload.Span,
                        TerminalJsonContext.Default.ExitResponse);
                    yield return new TerminalExited(exit?.ExitCode);
                    yield break;
                case ServerError:
                    var error = JsonSerializer.Deserialize(
                        frame.Payload.Span,
                        TerminalJsonContext.Default.ErrorResponse);
                    yield return new TerminalBrokerError(
                        error?.Code ?? "terminal.broker_error",
                        error?.Message ?? "Terminal broker rejected the request");
                    yield break;
                default:
                    throw new IOException($"Unknown terminal broker frame type: {frame.Type}");
            }
        }
    }

    public Task SendInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (data.IsEmpty || data.Length > MaximumDataFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        return WriteFrameAsync(ClientStdin, data, cancellationToken);
    }

    public Task ResizeAsync(ushort columns, ushort rows, CancellationToken cancellationToken)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload, columns);
        BinaryPrimitives.WriteUInt16BigEndian(payload[2..], rows);
        return WriteFrameAsync(ClientResize, payload.ToArray(), cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (closed)
        {
            return;
        }

        closed = true;
        try
        {
            await WriteFrameAsync(ClientClose, ReadOnlyMemory<byte>.Empty, cancellationToken);
        }
        catch (IOException)
        {
            // The broker may close first when the shell exits.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync(CancellationToken.None);
        }
        finally
        {
            stream.Dispose();
            writeLock.Dispose();
        }
    }

    private async Task<BrokerFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaximumDataFrameBytes + 1)
        {
            throw new IOException("Terminal broker frame length is invalid");
        }

        var frame = new byte[checked((int)length)];
        await ReadExactlyAsync(frame, cancellationToken);
        return new BrokerFrame(frame[0], frame.AsMemory(1));
    }

    private async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var count = await stream.ReadAsync(destination[offset..], cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException("Terminal broker disconnected");
            }

            offset += count;
        }
    }

    private async Task WriteFrameAsync(
        byte type,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var frame = new byte[payload.Length + 1];
        frame[0] = type;
        payload.CopyTo(frame.AsMemory(1));
        await WriteLengthPrefixedAsync(frame, cancellationToken);
    }

    private async Task WriteLengthPrefixedAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private sealed record OpenRequest(
        ushort ProtocolVersion,
        Guid SessionId,
        string Profile,
        ushort Columns,
        ushort Rows);

    private sealed record OpenedResponse(ushort ProtocolVersion, Guid SessionId);

    private sealed record ExitResponse(int? ExitCode);

    private sealed record ErrorResponse(string Code, string Message);

    private sealed record BrokerFrame(byte Type, ReadOnlyMemory<byte> Payload);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(OpenRequest))]
    [JsonSerializable(typeof(OpenedResponse))]
    [JsonSerializable(typeof(ExitResponse))]
    [JsonSerializable(typeof(ErrorResponse))]
    private sealed partial class TerminalJsonContext : JsonSerializerContext;
}
