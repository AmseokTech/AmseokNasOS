//--------------------------//
//--------验证 C# 终端客户端的长度分帧与强类型事件映射---------//
//--------Verifies C# terminal framing and typed broker event mapping--------//
//-------------------------//
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Application.Terminal;
using Nas.Infrastructure.Terminal;

namespace Nas.Api.Tests;

public sealed class TerminalBrokerProtocolTests
{
    [Fact]
    public async Task ClientUsesTheVersionedBoundedBrokerProtocol()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;
        // Darwin 的 Unix socket 路径上限只有 104 字节，系统临时目录本身可能很长。
        var socketPath = Path.Combine("/tmp", $"atb-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        try
        {
            var sessionId = Guid.NewGuid();
            var server = RunFakeBrokerAsync(listener, sessionId, cancellationToken);
            var client = new UnixSocketTerminalBrokerClient(Options.Create(new TerminalOptions
            {
                SocketPath = socketPath
            }));

            await using var session = await client.OpenAsync(sessionId, 120, 32, cancellationToken);
            await session.SendInputAsync("whoami\r\n"u8.ToArray(), cancellationToken);
            await session.ResizeAsync(100, 28, cancellationToken);

            var events = new List<TerminalBrokerEvent>();
            await foreach (var brokerEvent in session.ReadEventsAsync(cancellationToken))
            {
                events.Add(brokerEvent);
            }

            var output = Assert.IsType<TerminalOutput>(events[0]);
            Assert.Equal("terminal-output", Encoding.UTF8.GetString(output.Data.Span));
            Assert.IsType<TerminalExited>(events[1]);
            await server;
        }
        finally
        {
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }
        }
    }

    private static async Task RunFakeBrokerAsync(
        Socket listener,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var openPayload = await ReadLengthPrefixedAsync(stream, cancellationToken);
        using var openDocument = JsonDocument.Parse(openPayload);
        Assert.Equal(1, openDocument.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(expectedSessionId, openDocument.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal("maintenance", openDocument.RootElement.GetProperty("profile").GetString());

        await WriteFrameAsync(
            stream,
            0x10,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                protocolVersion = 1,
                sessionId = expectedSessionId
            }),
            cancellationToken);

        var input = await ReadTypedFrameAsync(stream, cancellationToken);
        Assert.Equal(0x01, input.Type);
        Assert.Equal("whoami\r\n", Encoding.UTF8.GetString(input.Payload));
        var resize = await ReadTypedFrameAsync(stream, cancellationToken);
        Assert.Equal(0x02, resize.Type);
        Assert.Equal((ushort)100, BinaryPrimitives.ReadUInt16BigEndian(resize.Payload));
        Assert.Equal((ushort)28, BinaryPrimitives.ReadUInt16BigEndian(resize.Payload.AsSpan(2)));

        await WriteFrameAsync(stream, 0x11, "terminal-output"u8.ToArray(), cancellationToken);
        await WriteFrameAsync(
            stream,
            0x12,
            JsonSerializer.SerializeToUtf8Bytes(new { exitCode = 0 }),
            cancellationToken);
    }

    private static async Task<byte[]> ReadLengthPrefixedAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    private static async Task<(byte Type, byte[] Payload)> ReadTypedFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var frame = await ReadLengthPrefixedAsync(stream, cancellationToken);
        return (frame[0], frame[1..]);
    }

    private static async Task WriteFrameAsync(
        NetworkStream stream,
        byte type,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length + 1));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(new[] { type }, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
