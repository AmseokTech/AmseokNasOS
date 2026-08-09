//--------------------------//
//--------验证数据卷 Operation 持久化、资源锁与审计意图---------//
//--------Verifies volume-operation persistence, resource locks, and audit intents--------//
//-------------------------//
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nas.Application.StorageManagement;
using Nas.Domain.Operations;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Api.Tests;

public sealed class SqliteStorageOperationStoreTests
{
    [Fact]
    public async Task StartPersistsArrayAndVolumeLocksAndRejectsOverlap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>().UseSqlite(connection).Options;
        await using var database = new NodeDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var store = new SqliteStorageOperationStore(database, TimeProvider.System);
        var userId = Guid.NewGuid();

        var started = await store.StartAsync(
            userId,
            Ticket(userId, ["array:md:test", "volume:data"]),
            "provision-data-1",
            CancellationToken.None);

        var operation = Assert.IsType<StorageOperationStarted>(started).Operation;
        Assert.Equal(2, await database.ResourceLocks.CountAsync());
        Assert.Equal("audit.storage.operation", (await database.OutboxMessages.SingleAsync()).Subject);
        var overlapping = await store.StartAsync(
            userId,
            Ticket(userId, ["volume:data"]),
            "permission-data-1",
            CancellationToken.None);
        Assert.Equal(
            "storage.resource_locked",
            Assert.IsType<StorageOperationStartRejected>(overlapping).Code);

        var volume = ManagedVolume();
        var completed = await store.RecordExecutionAsync(
            operation.Id,
            OperationStatus.Succeeded,
            volume,
            null,
            false,
            true,
            CancellationToken.None);
        Assert.Equal(OperationStatus.Succeeded, completed.Status);
        Assert.Equal("filesystem-uuid", completed.Volume?.FileSystemUuid);
        Assert.Empty(await database.ResourceLocks.ToArrayAsync());
        Assert.Equal(2, await database.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ReusingAnIdempotencyKeyWithDifferentStateIsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>().UseSqlite(connection).Options;
        await using var database = new NodeDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var store = new SqliteStorageOperationStore(database, TimeProvider.System);
        var userId = Guid.NewGuid();
        await store.StartAsync(
            userId,
            Ticket(userId, ["volume:data"], new string('a', 64)),
            "same-key",
            CancellationToken.None);

        var repeated = await store.StartAsync(
            userId,
            Ticket(userId, ["volume:other"], new string('b', 64)),
            "same-key",
            CancellationToken.None);

        Assert.Equal(
            "storage.idempotency_conflict",
            Assert.IsType<StorageOperationStartRejected>(repeated).Code);
    }

    private static StoragePreviewTicket Ticket(
        Guid userId,
        IReadOnlyList<string> resources,
        string? fingerprint = null) => new(
            Guid.NewGuid().ToString("N"),
            userId,
            new RequestedStorageOperation(
                StorageOperationKind.ProvisionVolume,
                "md:test",
                null,
                "data",
                "root",
                "amseoknas-data",
                "0770",
                new SmbShareSettings(false, null, false, false, null),
                new NfsShareSettings(false, null, false)),
            resources,
            fingerprint ?? new string('a', 64),
            "格式化 md0",
            DateTimeOffset.UtcNow.AddMinutes(2));

    private static ManagedVolumeInformation ManagedVolume() => new(
        "volume:data",
        "data",
        "md:test",
        "/dev/md0",
        "filesystem-uuid",
        "ext4",
        "/srv/amseoknas/volumes/data",
        true,
        true,
        "root",
        "amseoknas-data",
        "0770",
        true,
        new SmbShareSettings(false, null, false, false, null),
        new NfsShareSettings(false, null, false));
}
