//--------------------------//
//--------通过有界强类型 Unix Socket 协议访问 Rust 查询守护进程---------//
//--------Uses a bounded typed Unix socket protocol to query the Rust daemon--------//
//-------------------------//
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Application.SystemSettings;

namespace Nas.Infrastructure.Privileged;

public sealed class UnixSocketPrivilegedClient(
    IOptions<PrivilegedOptions> options,
    TimeProvider timeProvider) : IPrivilegedClient
{
    private const ushort ProtocolVersion = 1;
    private const int MaximumFrameBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SystemAboutInformation> GetAboutAsync(CancellationToken cancellationToken)
    {
        return SendAsync<SystemAboutInformation>("system.getAbout", cancellationToken);
    }

    public async Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
        CancellationToken cancellationToken)
    {
        return await SendAsync<NetworkInterfaceInformation[]>(
            "network.inspectInterfaces",
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(string action, CancellationToken cancellationToken)
        where T : class
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            throw new PrivilegedClientException(
                "privileged.disabled",
                "底层系统查询服务尚未启用",
                false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        var requestId = Guid.NewGuid().ToString("D");
        var request = new RequestEnvelope(
            ProtocolVersion,
            requestId,
            action,
            timeProvider.GetUtcNow()
                .AddSeconds(configuration.TimeoutSeconds)
                .ToUnixTimeMilliseconds(),
            new Dictionary<string, object?>());
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        if (payload.Length > MaximumFrameBytes)
        {
            throw new InvalidOperationException("Privileged request exceeds the protocol limit");
        }

        try
        {
            using var socket = new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified);
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint(configuration.SocketPath),
                timeout.Token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await WriteFrameAsync(stream, payload, timeout.Token);
            var responsePayload = await ReadFrameAsync(stream, timeout.Token);
            var response = JsonSerializer.Deserialize<ResponseEnvelope<T>>(
                responsePayload,
                JsonOptions)
                ?? throw new IOException("Privileged daemon returned an empty response");

            if (response.ProtocolVersion != ProtocolVersion
                || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            {
                throw new IOException("Privileged daemon response did not match the request");
            }
            if (!response.Success)
            {
                var errorCode = response.Error?.Code ?? "privileged.rejected";
                throw new PrivilegedClientException(
                    errorCode,
                    PublicErrorMessage(errorCode),
                    response.Error?.Retryable ?? false,
                    diagnosticMessage: response.Error?.Message);
            }

            return response.Result
                ?? throw new IOException("Privileged daemon returned no result");
        }
        catch (PrivilegedClientException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SocketException
                or IOException
                or OperationCanceledException)
        {
            throw new PrivilegedClientException(
                "privileged.unavailable",
                "底层系统查询服务当前不可用",
                true,
                exception);
        }
    }

    private static string PublicErrorMessage(string code)
    {
        return code switch
        {
            "protocol.unsupported_version" => "底层系统查询服务协议不兼容",
            "request.invalid" => "底层系统查询请求无效",
            "request.deadline_exceeded" => "底层系统查询已超时",
            "inventory.read_failed" => "底层系统信息读取失败",
            _ => "底层系统查询被拒绝"
        };
    }

    private static async Task WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaximumFrameBytes)
        {
            throw new IOException("Privileged daemon frame length is invalid");
        }

        var payload = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    private sealed record RequestEnvelope(
        ushort ProtocolVersion,
        string RequestId,
        string Action,
        long DeadlineUnixMilliseconds,
        IReadOnlyDictionary<string, object?> Parameters);

    private sealed record ResponseEnvelope<T>(
        ushort ProtocolVersion,
        string RequestId,
        bool Success,
        T? Result,
        ResponseError? Error);

    private sealed record ResponseError(string Code, string Message, bool Retryable);
}
