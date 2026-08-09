//--------------------------//
//--------验证 RAID Operation 可原子持有多个资源锁---------//
//--------Verifies that a RAID operation atomically owns multiple resource locks--------//
//-------------------------//
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nas.Application.RaidManagement;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Api.Tests;

public sealed class SqliteRaidOperationStoreTests
{
    [Fact]
    public async Task NodeMigrationsApplyTheNonUniqueResourceLockIndex()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new NodeDbContext(options);

        await database.Database.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA index_list('ResourceLocks')";
        await using var reader = await command.ExecuteReaderAsync();
        var found = false;
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1) == "IX_ResourceLocks_OperationId")
            {
                found = true;
                Assert.Equal(0L, reader.GetInt64(2));
            }
        }
        Assert.True(found);
    }

    [Fact]
    public async Task StartPersistsAllLocksAndRejectsAnOverlappingOperation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new NodeDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var store = new SqliteRaidOperationStore(database, TimeProvider.System);
        var userId = Guid.NewGuid();
        var firstTicket = Ticket(userId, ["array:md:test", "disk:wwn:a", "disk:wwn:b"]);

        var first = await store.StartAsync(userId, firstTicket, "operation-1", CancellationToken.None);

        Assert.IsType<RaidOperationStarted>(first);
        Assert.Equal(3, await database.ResourceLocks.CountAsync());
        var second = await store.StartAsync(
            userId,
            Ticket(userId, ["disk:wwn:b", "disk:wwn:c"]),
            "operation-2",
            CancellationToken.None);
        Assert.Equal("raid.resource_locked", Assert.IsType<RaidOperationStartRejected>(second).Code);
    }

    [Fact]
    public async Task ReusingAnIdempotencyKeyWithADifferentSnapshotIsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new NodeDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var store = new SqliteRaidOperationStore(database, TimeProvider.System);
        var userId = Guid.NewGuid();
        await store.StartAsync(
            userId,
            Ticket(userId, ["disk:wwn:a"], fingerprint: new string('a', 64)),
            "same-key",
            CancellationToken.None);

        var repeated = await store.StartAsync(
            userId,
            Ticket(userId, ["disk:wwn:b"], fingerprint: new string('b', 64)),
            "same-key",
            CancellationToken.None);

        Assert.Equal("raid.idempotency_conflict", Assert.IsType<RaidOperationStartRejected>(repeated).Code);
    }

    private static RaidPreviewTicket Ticket(
        Guid userId,
        IReadOnlyList<string> resources,
        string? fingerprint = null) => new(
            Guid.NewGuid().ToString("N"),
            userId,
            new RequestedRaidOperation(
                RaidOperationKind.Delete,
                "md:test",
                null,
                null,
                [],
                null,
                null),
            "md0",
            ["wwn:a", "wwn:b"],
            resources,
            fingerprint ?? new string('a', 64),
            "删除 md0",
            DateTimeOffset.UtcNow.AddMinutes(2));
}
