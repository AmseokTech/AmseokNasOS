//--------------------------//
//--------验证 C# 与 Rust 只读守护进程的有界协议---------//
//--------Verifies the bounded C# to Rust read-only daemon protocol--------//
//-------------------------//
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Application.SystemSettings;
using Nas.Infrastructure.Privileged;

namespace Nas.Api.Tests;

public sealed class PrivilegedClientProtocolTests
{
    [Fact]
    public async Task AboutQueryUsesTheRegisteredVersionedAction()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"amseoknas-privileged-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        try
        {
            var server = RunFakeDaemonAsync(listener, cancellationToken);
            var client = new UnixSocketPrivilegedClient(
                Options.Create(new PrivilegedOptions
                {
                    Enabled = true,
                    SocketPath = socketPath,
                    TimeoutSeconds = 5
                }),
                TimeProvider.System);

            var about = await client.GetAboutAsync(cancellationToken);

            Assert.Equal("nas-test", about.HostName);
            Assert.Equal("Test CPU", about.Cpu.Model);
            Assert.Equal(32L * 1024 * 1024 * 1024, about.Memory.TotalBytes);
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

    [Fact]
    public async Task DisabledClientFailsClosedBeforeConnecting()
    {
        var client = new UnixSocketPrivilegedClient(
            Options.Create(new PrivilegedOptions
            {
                Enabled = false,
                SocketPath = "/path/that/must/not/be-used.sock",
                TimeoutSeconds = 5
            }),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<PrivilegedClientException>(
            () => client.GetAboutAsync(CancellationToken.None));

        Assert.Equal("privileged.disabled", exception.Code);
        Assert.False(exception.Retryable);
    }

    private static async Task RunFakeDaemonAsync(
        Socket listener,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var requestPayload = await ReadFrameAsync(stream, cancellationToken);
        using var request = JsonDocument.Parse(requestPayload);
        var root = request.RootElement;
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("system.getAbout", root.GetProperty("action").GetString());
        Assert.Empty(root.GetProperty("parameters").EnumerateObject());
        Assert.True(
            root.GetProperty("deadlineUnixMilliseconds").GetInt64()
                > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            requestId = root.GetProperty("requestId").GetString(),
            success = true,
            result = new
            {
                hostName = "nas-test",
                operatingSystem = "AmseokOS",
                kernelVersion = "6.12.0",
                uptimeSeconds = 3600,
                cpu = new
                {
                    model = "Test CPU",
                    physicalCoreCount = 4,
                    logicalProcessorCount = 8,
                    currentFrequencyMhz = 2400,
                    maximumFrequencyMhz = 4200
                },
                memory = new { totalBytes = 32L * 1024 * 1024 * 1024 },
                systemStorage = new
                {
                    source = "/dev/test",
                    stableId = "test-disk",
                    model = "Test Disk",
                    totalBytes = 1024L,
                    usedBytes = 512L,
                    availableBytes = 512L
                }
            },
            error = (object?)null,
            diagnostics = new { durationMs = 1, truncated = false }
        });
        await WriteFrameAsync(stream, response, cancellationToken);
    }

    private static async Task<byte[]> ReadFrameAsync(
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

    private static async Task WriteFrameAsync(
        NetworkStream stream,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
