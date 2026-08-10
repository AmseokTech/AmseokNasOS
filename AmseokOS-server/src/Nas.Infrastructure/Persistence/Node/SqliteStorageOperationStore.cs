//--------------------------//
//--------持久化数据卷 Operation、资源锁、fencing 与审计意图---------//
//--------Persists volume operations, locks, fencing, and audit intents--------//
//-------------------------//
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nas.Application.StorageManagement;
using Nas.Domain.Operations;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class SqliteStorageOperationStore(
    NodeDbContext database,
    TimeProvider timeProvider) : IStorageOperationStore
{
    private static readonly Guid LocalNodeId = Guid.Empty;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaximumOperationLease = TimeSpan.FromDays(7);

    public async Task<StorageOperationStartOutcome> StartAsync(
        Guid userId,
        StoragePreviewTicket ticket,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await database.Operations.SingleOrDefaultAsync(
            operation => operation.NodeId == LocalNodeId
                && operation.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.Type.StartsWith("storage.", StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StorageOperationStartRejected("storage.idempotency_conflict");
            }
            var checkpoint = DeserializeCheckpoint(existing);
            await transaction.RollbackAsync(cancellationToken);
            if (checkpoint.UserId != userId
                || !string.Equals(
                    checkpoint.SnapshotFingerprint,
                    ticket.SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                return new StorageOperationStartRejected("storage.idempotency_conflict");
            }
            return new StorageOperationAlreadyExists(ToApplication(existing));
        }

        var now = timeProvider.GetUtcNow();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ResourceLocks WHERE LeaseExpiresAt <= {now}",
            cancellationToken);
        var lockedResources = await database.ResourceLocks
            .Where(resourceLock => ticket.ResourceIds.Contains(resourceLock.ResourceId))
            .Select(resourceLock => resourceLock.ResourceId)
            .ToArrayAsync(cancellationToken);
        if (lockedResources.Length > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new StorageOperationStartRejected("storage.resource_locked");
        }

        var nodeState = await database.NodeStates.SingleOrDefaultAsync(
            state => state.NodeId == LocalNodeId,
            cancellationToken);
        if (nodeState is null)
        {
            nodeState = new NodeDatabaseState
            {
                NodeId = LocalNodeId,
                ClusterId = Guid.Empty,
                AcceptedFencingToken = 0,
                Version = 0,
                UpdatedAt = now
            };
            database.NodeStates.Add(nodeState);
        }
        nodeState.AcceptedFencingToken = checked(nodeState.AcceptedFencingToken + 1);
        nodeState.Version = checked(nodeState.Version + 1);
        nodeState.UpdatedAt = now;

        var operationId = Guid.NewGuid();
        var resourceId = ticket.ResourceIds.FirstOrDefault()
            ?? $"storage-operation:{operationId:D}";
        var checkpointValue = new StorageCheckpoint(
            userId,
            ticket.Requested.Kind,
            ticket.Requested,
            ticket.SnapshotFingerprint,
            null,
            false);
        var operation = new LocalOperation
        {
            Id = operationId,
            NodeId = LocalNodeId,
            Type = $"storage.{ticket.Requested.Kind.ToString().ToLowerInvariant()}",
            ResourceId = resourceId,
            IdempotencyKey = idempotencyKey,
            Status = OperationStatus.Running,
            FencingToken = nodeState.AcceptedFencingToken,
            Checkpoint = JsonSerializer.Serialize(checkpointValue, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
        database.Operations.Add(operation);
        database.ResourceLocks.AddRange(ticket.ResourceIds.Select(resource => new ResourceLock
        {
            ResourceId = resource,
            OperationId = operationId,
            FencingToken = nodeState.AcceptedFencingToken,
            LeaseExpiresAt = now.Add(MaximumOperationLease)
        }));
        AddAuditIntent(operation, userId, "started", now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StorageOperationStarted(ToApplication(operation));
    }

    public async Task<StorageOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId && item.Type.StartsWith("storage."),
            cancellationToken);
        return operation is null ? null : ToApplication(operation);
    }

    public async Task<StorageOperation> RecordExecutionAsync(
        Guid operationId,
        OperationStatus status,
        ManagedVolumeInformation? volume,
        string? errorCode,
        bool retryable,
        bool releaseLocks,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var operation = await database.Operations.SingleOrDefaultAsync(
            item => item.Id == operationId && item.Type.StartsWith("storage."),
            cancellationToken)
            ?? throw new InvalidOperationException("Storage operation disappeared during execution");
        var checkpoint = DeserializeCheckpoint(operation) with
        {
            Volume = volume,
            Retryable = retryable
        };
        operation.Status = status;
        operation.ErrorCode = errorCode;
        operation.Checkpoint = JsonSerializer.Serialize(checkpoint, JsonOptions);
        operation.UpdatedAt = timeProvider.GetUtcNow();
        if (releaseLocks)
        {
            await database.ResourceLocks
                .Where(resourceLock => resourceLock.OperationId == operationId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        AddAuditIntent(
            operation,
            checkpoint.UserId,
            status.ToString().ToLowerInvariant(),
            operation.UpdatedAt.Value);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToApplication(operation);
    }

    private void AddAuditIntent(
        LocalOperation operation,
        Guid userId,
        string outcome,
        DateTimeOffset occurredAt)
    {
        database.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            Subject = "audit.storage.operation",
            PayloadJson = JsonSerializer.Serialize(new
            {
                operationId = operation.Id,
                userId,
                operation.Type,
                operation.ResourceId,
                outcome,
                errorCode = operation.ErrorCode
            }, JsonOptions),
            OccurredAt = occurredAt
        });
    }

    private static StorageOperation ToApplication(LocalOperation operation)
    {
        var checkpoint = DeserializeCheckpoint(operation);
        return new StorageOperation(
            operation.Id,
            checkpoint.UserId,
            checkpoint.Kind,
            checkpoint.Requested,
            operation.Status,
            operation.ResourceId,
            operation.IdempotencyKey,
            operation.FencingToken,
            checkpoint.Volume,
            operation.ErrorCode,
            checkpoint.Retryable,
            operation.CreatedAt,
            operation.UpdatedAt);
    }

    private static StorageCheckpoint DeserializeCheckpoint(LocalOperation operation) =>
        JsonSerializer.Deserialize<StorageCheckpoint>(operation.Checkpoint ?? "", JsonOptions)
        ?? throw new InvalidOperationException("Storage operation checkpoint is invalid");

    private sealed record StorageCheckpoint(
        Guid UserId,
        StorageOperationKind Kind,
        RequestedStorageOperation Requested,
        string SnapshotFingerprint,
        ManagedVolumeInformation? Volume,
        bool Retryable);
}
