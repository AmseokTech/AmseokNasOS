//--------------------------//
//--------验证 C# 与 Rust 只读守护进程的有界协议---------//
//--------Verifies the bounded C# to Rust read-only daemon protocol--------//
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
using Nas.Infrastructure.Privileged;

namespace Nas.Api.Tests;

public sealed class PrivilegedClientProtocolTests
{
    [Fact]
    public async Task AboutQueryUsesTheRegisteredVersionedAction()
    {
        var about = await QueryFakeDaemonAsync(
            "system.getAbout",
            new
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
            (client, token) => client.GetAboutAsync(token));

        Assert.Equal("nas-test", about.HostName);
        Assert.Equal("Test CPU", about.Cpu.Model);
        Assert.Equal(32L * 1024 * 1024 * 1024, about.Memory.TotalBytes);
    }

    [Fact]
    public async Task NetworkConfigurationInventoryUsesTheRegisteredReadAction()
    {
        var interfaces = await QueryFakeDaemonAsync(
            "network.inspectInterfaces",
            new[]
            {
                new
                {
                    id = "mac:00:11:22:33:44:55",
                    name = "enp1s0",
                    configurationMode = "dhcp",
                    addresses = new[] { "192.168.1.10/24" },
                    gateway = "192.168.1.1"
                }
            },
            (client, token) => client.InspectInterfacesAsync(token));

        var networkInterface = Assert.Single(interfaces);
        Assert.Equal("mac:00:11:22:33:44:55", networkInterface.Id);
        Assert.Equal("dhcp", networkInterface.ConfigurationMode);
        Assert.Equal("192.168.1.10/24", Assert.Single(networkInterface.Addresses));
    }

    [Fact]
    public async Task BlockDeviceQueryUsesTheRegisteredVersionedAction()
    {
        var devices = await QueryFakeDaemonAsync(
            "storage.inspectBlockDevices",
            new[]
            {
                new
                {
                    id = "wwn:test",
                    stable = true,
                    identityConflict = false,
                    topologyComplete = true,
                    name = "sda",
                    path = "/dev/sda",
                    model = "Test Disk",
                    serialNumber = "SERIAL",
                    wwn = "WWN",
                    sizeBytes = 4096L,
                    logicalSectorBytes = 512L,
                    physicalSectorBytes = 4096L,
                    rotational = true,
                    removable = false,
                    readOnly = false,
                    partitions = Array.Empty<object>(),
                    mountPoints = new[] { "/" },
                    systemDevice = true,
                    swap = false,
                    raidMember = false,
                    inUse = true,
                    dependentDevices = new[]
                    {
                        new
                        {
                            name = "dm-1",
                            path = "/dev/dm-1",
                            kind = "lvm",
                            mountPoints = new[] { "/" },
                            swap = false
                        }
                    }
                }
            },
            (client, token) => client.GetBlockDevicesAsync(token));

        var device = Assert.Single(devices);
        Assert.Equal("wwn:test", device.Id);
        Assert.True(device.TopologyComplete);
        Assert.True(device.SystemDevice);
        Assert.True(device.InUse);
        Assert.Equal("lvm", Assert.Single(device.DependentDevices!).Kind);
    }

    [Fact]
    public async Task MissingTopologyFieldsFromAnOlderDaemonFailClosed()
    {
        var devices = await QueryFakeDaemonAsync(
            "storage.inspectBlockDevices",
            new[]
            {
                new
                {
                    id = "wwn:old-daemon",
                    stable = true,
                    name = "sda",
                    path = "/dev/sda",
                    model = "Old Test Disk",
                    serialNumber = "SERIAL",
                    wwn = "WWN",
                    sizeBytes = 4096L,
                    logicalSectorBytes = 512L,
                    physicalSectorBytes = 4096L,
                    rotational = true,
                    removable = false,
                    readOnly = false,
                    partitions = Array.Empty<object>(),
                    mountPoints = Array.Empty<string>(),
                    systemDevice = false,
                    swap = false,
                    raidMember = false
                }
            },
            (client, token) => client.GetBlockDevicesAsync(token));

        var device = Assert.Single(devices);
        Assert.False(device.TopologyComplete);
        Assert.True(device.InUse);
        Assert.Empty(device.DependentDevices);
    }

    [Fact]
    public async Task RaidArrayQueryUsesTheRegisteredVersionedAction()
    {
        var arrays = await QueryFakeDaemonAsync(
            "raid.inspectArrays",
            new[]
            {
                new
                {
                    id = "md:test",
                    name = "md0",
                    path = "/dev/md0",
                    uuid = "test",
                    level = "raid1",
                    state = "active",
                    metadataVersion = "1.2",
                    sizeBytes = 4096L,
                    configuredDeviceCount = 2L,
                    degradedDeviceCount = 0L,
                    syncAction = "idle",
                    syncCompletedSectors = (long?)null,
                    syncTotalSectors = (long?)null,
                    members = new[]
                    {
                        new
                        {
                            name = "sda1",
                            path = "/dev/sda1",
                            state = "in_sync",
                            slot = (int?)0
                        }
                    }
                }
            },
            (client, token) => client.GetRaidArraysAsync(token));

        var array = Assert.Single(arrays);
        Assert.Equal("raid1", array.Level);
        Assert.Equal("sda1", Assert.Single(array.Members).Name);
    }

    [Fact]
    public async Task RaidCreateUsesTheTypedWhitelistedActionAndLongWriteDeadline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var socketPath = Path.Combine("/tmp", $"anp-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        try
        {
            var server = RunRaidFakeDaemonAsync(listener, timeout.Token);
            var client = new UnixSocketPrivilegedClient(
                Options.Create(new PrivilegedOptions
                {
                    Enabled = true,
                    SocketPath = socketPath,
                    TimeoutSeconds = 5,
                    RaidTimeoutSeconds = 60
                }),
                TimeProvider.System);
            var operationId = Guid.NewGuid();

            var outcome = await ((IRaidCommandExecutor)client).ExecuteAsync(
                new RaidExecutionCommand(
                    operationId,
                    "create-data-1",
                    7,
                    new RequestedRaidOperation(
                        RaidOperationKind.Create,
                        null,
                        "data",
                        "raid1",
                        ["wwn:a", "wwn:b"],
                        null,
                        null),
                    [],
                    new string('a', 64)),
                timeout.Token);

            var accepted = Assert.IsType<RaidExecutionAccepted>(outcome);
            Assert.Equal("md:created", accepted.ArrayId);
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

    [Fact]
    public async Task RaidConnectFailureIsClassifiedAsNotDispatched()
    {
        var client = new UnixSocketPrivilegedClient(
            Options.Create(new PrivilegedOptions
            {
                Enabled = true,
                SocketPath = $"/tmp/missing-{Guid.NewGuid():N}.sock",
                TimeoutSeconds = 5,
                RaidTimeoutSeconds = 60
            }),
            TimeProvider.System);

        var outcome = await ((IRaidCommandExecutor)client).ExecuteAsync(
            new RaidExecutionCommand(
                Guid.NewGuid(),
                "not-dispatched",
                1,
                new RequestedRaidOperation(
                    RaidOperationKind.Create,
                    null,
                    "data",
                    "raid1",
                    ["wwn:a", "wwn:b"],
                    null,
                    null),
                [],
                new string('a', 64)),
            CancellationToken.None);

        var rejected = Assert.IsType<RaidExecutionRejected>(outcome);
        Assert.Equal("privileged.unavailable_before_dispatch", rejected.Code);
        Assert.False(rejected.OutcomeUncertain);
    }

    private static async Task<T> QueryFakeDaemonAsync<T>(
        string expectedAction,
        object result,
        Func<UnixSocketPrivilegedClient, CancellationToken, Task<T>> query)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;
        var socketPath = Path.Combine("/tmp", $"anp-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        try
        {
            var server = RunFakeDaemonAsync(
                listener,
                expectedAction,
                result,
                cancellationToken);
            var client = new UnixSocketPrivilegedClient(
                Options.Create(new PrivilegedOptions
                {
                    Enabled = true,
                    SocketPath = socketPath,
                    TimeoutSeconds = 5
                }),
                TimeProvider.System);

            var queryResult = await query(client, cancellationToken);
            await server;
            return queryResult;
        }
        finally
        {
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }
        }
    }

    private static async Task RunFakeDaemonAsync(
        Socket listener,
        string expectedAction,
        object result,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var requestPayload = await ReadFrameAsync(stream, cancellationToken);
        using var request = JsonDocument.Parse(requestPayload);
        var root = request.RootElement;
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(expectedAction, root.GetProperty("action").GetString());
        Assert.Empty(root.GetProperty("parameters").EnumerateObject());
        Assert.True(
            root.GetProperty("deadlineUnixMilliseconds").GetInt64()
                > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            requestId = root.GetProperty("requestId").GetString(),
            success = true,
            result,
            error = (object?)null,
            diagnostics = new { durationMs = 1, truncated = false }
        });
        await WriteFrameAsync(stream, response, cancellationToken);
    }

    private static async Task RunRaidFakeDaemonAsync(
        Socket listener,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var requestPayload = await ReadFrameAsync(stream, cancellationToken);
        using var request = JsonDocument.Parse(requestPayload);
        var root = request.RootElement;
        Assert.Equal("raid.createArray", root.GetProperty("action").GetString());
        Assert.True(
            root.GetProperty("deadlineUnixMilliseconds").GetInt64()
                > DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeMilliseconds());
        var parameters = root.GetProperty("parameters");
        Assert.Equal("data", parameters.GetProperty("arrayName").GetString());
        Assert.Equal("raid1", parameters.GetProperty("level").GetString());
        Assert.Equal(7, parameters.GetProperty("fencingToken").GetInt64());
        Assert.Equal(2, parameters.GetProperty("deviceIds").GetArrayLength());

        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            requestId = root.GetProperty("requestId").GetString(),
            success = true,
            result = new
            {
                arrayId = "md:created",
                inProgress = false,
                progressPercentage = 100
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
