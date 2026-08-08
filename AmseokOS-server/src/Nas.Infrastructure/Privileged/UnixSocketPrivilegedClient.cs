//--------------------------//
//--------通过有界强类型 Unix Socket 协议访问 Rust 查询守护进程---------//
//--------Uses a bounded typed Unix socket protocol to query the Rust daemon--------//
//-------------------------//
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Application.NetworkConfiguration;
using Nas.Application.Privileged;
using Nas.Application.RaidManagement;
using Nas.Application.Storage;
using Nas.Application.SystemSettings;

namespace Nas.Infrastructure.Privileged;

public sealed class UnixSocketPrivilegedClient(
    IOptions<PrivilegedOptions> options,
    TimeProvider timeProvider) :
    ISystemSettingsClient,
    IStorageInventoryClient,
    INetworkConfigurationInventory,
    IRaidCommandExecutor
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

    public async Task<IReadOnlyList<NetworkConfigurationInterfaceSnapshot>> InspectInterfacesAsync(
        CancellationToken cancellationToken)
    {
        return await SendAsync<NetworkConfigurationInterfaceSnapshot[]>(
            "network.inspectInterfaces",
            cancellationToken);
    }

    public async Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(
        CancellationToken cancellationToken)
    {
        var devices = await SendAsync<BlockDeviceInformation[]>(
            "storage.inspectBlockDevices",
            cancellationToken);
        return devices.Select(NormalizeBlockDevice).ToArray();
    }

    public async Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(
        CancellationToken cancellationToken)
    {
        return await SendAsync<RaidArrayInformation[]>(
            "raid.inspectArrays",
            cancellationToken);
    }

    public async Task<RaidExecutionOutcome> ExecuteAsync(
        RaidExecutionCommand command,
        CancellationToken cancellationToken)
    {
        var action = command.Requested.Kind switch
        {
            RaidOperationKind.Create => "raid.createArray",
            RaidOperationKind.Delete => "raid.deleteArray",
            RaidOperationKind.AddDevice => "raid.addDevice",
            RaidOperationKind.RemoveDevice => "raid.removeDevice",
            RaidOperationKind.ReplaceDevice => "raid.replaceDevice",
            RaidOperationKind.Grow => "raid.growArray",
            RaidOperationKind.Shrink => "raid.shrinkArray",
            _ => throw new InvalidOperationException("Unsupported RAID operation kind")
        };
        var parameters = new RaidExecutionParameters(
            command.OperationId.ToString("D"),
            command.IdempotencyKey,
            command.FencingToken,
            command.Requested.ArrayId,
            command.Requested.ArrayName,
            command.Requested.Level,
            command.Requested.DeviceIds,
            command.Requested.SourceDeviceId,
            command.Requested.TargetDeviceCount,
            command.ExpectedMemberDeviceIds,
            command.SnapshotFingerprint);
        try
        {
            var result = await SendAsync<RaidExecutionResult>(
                action,
                parameters,
                options.Value.RaidTimeoutSeconds,
                cancellationToken);
            return new RaidExecutionAccepted(
                result.ArrayId,
                result.InProgress,
                result.ProgressPercentage);
        }
        catch (PrivilegedClientException exception)
        {
            var uncertain = exception.Code is "privileged.unavailable"
                or "request.deadline_exceeded"
                or "tool.timeout"
                or "operation.duplicate_requires_reconciliation";
            return new RaidExecutionRejected(
                exception.Code,
                exception.Retryable,
                uncertain);
        }
    }

    private async Task<T> SendAsync<T>(string action, CancellationToken cancellationToken)
        where T : class
    {
        return await SendAsync<T>(
            action,
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        string action,
        object parameters,
        CancellationToken cancellationToken)
        where T : class
    {
        return await SendAsync<T>(
            action,
            parameters,
            options.Value.TimeoutSeconds,
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        string action,
        object parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
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
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var requestId = Guid.NewGuid().ToString("D");
        var request = new RequestEnvelope(
            ProtocolVersion,
            requestId,
            action,
            timeProvider.GetUtcNow()
                .AddSeconds(timeoutSeconds)
                .ToUnixTimeMilliseconds(),
            parameters);
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        if (payload.Length > MaximumFrameBytes)
        {
            throw new InvalidOperationException("Privileged request exceeds the protocol limit");
        }

        var connected = false;
        try
        {
            using var socket = new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified);
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint(configuration.SocketPath),
                timeout.Token);
            connected = true;
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
                connected
                    ? "privileged.unavailable"
                    : "privileged.unavailable_before_dispatch",
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
            "resource.not_found" => "RAID 目标资源不存在",
            "resource.identity_changed" => "RAID 目标身份已经变化",
            "resource.system_disk" => "系统盘禁止用于 RAID 写操作",
            "resource.busy" => "RAID 目标资源正在使用中",
            "tool.not_available" => "mdadm 工具不可用",
            "tool.timeout" => "mdadm 操作超时",
            "tool.failed" => "mdadm 操作失败",
            "result.verification_failed" => "RAID 操作结果复核失败",
            _ => "底层系统查询被拒绝"
        };
    }

    private static BlockDeviceInformation NormalizeBlockDevice(
        BlockDeviceInformation device)
    {
        var partitions = (device.Partitions ?? [])
            .Select(partition => partition with
            {
                // 旧 daemon 不包含拓扑字段；缺失信息必须按占用处理，不能默认为空闲
                InUse = partition.InUse || !partition.TopologyComplete,
                DependentDevices = partition.DependentDevices ?? []
            })
            .ToArray();
        return device with
        {
            InUse = device.InUse || !device.TopologyComplete,
            Partitions = partitions,
            DependentDevices = device.DependentDevices ?? []
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
        object Parameters);

    private sealed record ResponseEnvelope<T>(
        ushort ProtocolVersion,
        string RequestId,
        bool Success,
        T? Result,
        ResponseError? Error);

    private sealed record ResponseError(string Code, string Message, bool Retryable);

    private sealed record RaidExecutionParameters(
        string OperationId,
        string IdempotencyKey,
        long FencingToken,
        string? ArrayId,
        string? ArrayName,
        string? Level,
        IReadOnlyList<string> DeviceIds,
        string? SourceDeviceId,
        int? TargetDeviceCount,
        IReadOnlyList<string> ExpectedMemberDeviceIds,
        string SnapshotFingerprint);

    private sealed record RaidExecutionResult(
        string? ArrayId,
        bool InProgress,
        int? ProgressPercentage);
}
