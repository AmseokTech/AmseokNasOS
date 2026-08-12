//--------------------------//
//--------验证网络配置操作持久化、网卡锁与审计意图---------//
//--------Verifies network-operation persistence, interface locks, and audit intents--------//
//-------------------------//
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nas.Application.NetworkConfiguration;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Api.Tests;

public sealed class SqliteNetworkConfigurationOperationStoreTests
{
    [Fact]
    public async Task StartLocksTheInterfaceAndTerminalStateReleasesIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>().UseSqlite(connection).Options;
        await using var database = new NodeDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var store = new SqliteNetworkConfigurationOperationStore(database, TimeProvider.System);
        var userId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);

        var started = await store.StartAsync(
            userId,
            "mac:00:11:22:33:44:55",
            Configuration(),
            deadline,
            CancellationToken.None);

        var operation = Assert.IsType<NetworkConfigurationOperationStarted>(started).Operation;
        Assert.Equal(userId, operation.UserId);
        Assert.Equal(StoredNetworkConfigurationOperationState.Applying, operation.State);
        Assert.Single(await database.ResourceLocks.ToArrayAsync());
        Assert.Equal(
            "audit.network.configuration",
            (await database.OutboxMessages.SingleAsync()).Subject);

        var overlapping = await store.StartAsync(
            Guid.NewGuid(),
            "MAC:00:11:22:33:44:55",
            Configuration(),
            deadline,
            CancellationToken.None);
        Assert.Equal(
            "network.resource_locked",
            Assert.IsType<NetworkConfigurationOperationStartRejected>(overlapping).Code);

        await store.RecordAsync(
            operation.Id,
            StoredNetworkConfigurationOperationState.Confirmed,
            null,
            releaseLock: true,
            CancellationToken.None);

        var persisted = await store.GetAsync(operation.Id, CancellationToken.None);
        Assert.Equal(StoredNetworkConfigurationOperationState.Confirmed, persisted?.State);
        Assert.Empty(await database.ResourceLocks.ToArrayAsync());
        Assert.Equal(2, await database.OutboxMessages.CountAsync());
    }

    private static NormalizedNetworkConfiguration Configuration() => new(
        NetworkAddressingMode.StaticIpv4,
        "192.168.1.20",
        "255.255.255.0",
        24,
        "192.168.1.1");
}
