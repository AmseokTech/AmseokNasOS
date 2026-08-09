//--------------------------//
//--------持久化 RAID Operation、fencing token、资源锁与审计意图---------//
//--------Persists RAID operations, fencing tokens, resource locks, and audit intents--------//
//-------------------------//
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nas.Application.RaidManagement;
using Nas.Domain.Operations;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class SqliteRaidOperationStore(
    NodeDbContext database,
    TimeProvider timeProvider) : IRaidOperationStore
{
    private static readonly Guid LocalNodeId = Guid.Empty;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaximumOperationLease = TimeSpan.FromDays(7);

    public async Task<RaidOperationStartOutcome> StartAsync(
        Guid userId,
        RaidPreviewTicket ticket,
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
            var existingCheckpoint = DeserializeCheckpoint(existing);
            await transaction.RollbackAsync(cancellationToken);
            if (existingCheckpoint.UserId != userId
                || !string.Equals(
                    existingCheckpoint.SnapshotFingerprint,
                    ticket.SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                return new RaidOperationStartRejected("raid.idempotency_conflict");
            }
            return new RaidOperationAlreadyExists(ToApplication(existing));
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
            return new RaidOperationStartRejected("raid.resource_locked");
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
                UpdatedAt = timeProvider.GetUtcNow()
            };
            database.NodeStates.Add(nodeState);
        }
        nodeState.AcceptedFencingToken = checked(nodeState.AcceptedFencingToken + 1);
        nodeState.Version = checked(nodeState.Version + 1);
        nodeState.UpdatedAt = timeProvider.GetUtcNow();

        var operationId = Guid.NewGuid();
        var createdAt = timeProvider.GetUtcNow();
        var resourceId = ticket.ResourceIds.FirstOrDefault() ?? $"raid-operation:{operationId:D}";
        var checkpoint = new RaidCheckpoint(
            userId,
            ticket.Requested.Kind,
            ArrayId: ticket.Requested.ArrayId,
            ticket.Requested,
            ticket.SnapshotFingerprint,
            Retryable: false,
            ProgressPercentage: null);
        var operation = new LocalOperation
        {
            Id = operationId,
            NodeId = LocalNodeId,
            Type = $"raid.{ticket.Requested.Kind.ToString().ToLowerInvariant()}",
            ResourceId = resourceId,
            IdempotencyKey = idempotencyKey,
            Status = OperationStatus.Running,
            FencingToken = nodeState.AcceptedFencingToken,
            Checkpoint = JsonSerializer.Serialize(checkpoint, JsonOptions),
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        database.Operations.Add(operation);
        database.ResourceLocks.AddRange(ticket.ResourceIds.Select(resource => new ResourceLock
        {
            ResourceId = resource,
            OperationId = operationId,
            FencingToken = nodeState.AcceptedFencingToken,
            LeaseExpiresAt = createdAt.Add(MaximumOperationLease)
        }));
        AddAuditIntent(operation, userId, "started", createdAt);

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RaidOperationStarted(ToApplication(operation));
    }

    public async Task<RaidOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId,
            cancellationToken);
        return operation is null ? null : ToApplication(operation);
    }

    public async Task<RaidOperation> RecordExecutionAsync(
        Guid operationId,
        OperationStatus status,
        string? arrayId,
        string? errorCode,
        bool retryable,
        int? progressPercentage,
        bool releaseLocks,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var operation = await database.Operations.SingleOrDefaultAsync(
            item => item.Id == operationId,
            cancellationToken)
            ?? throw new InvalidOperationException("RAID operation disappeared during execution");
        var checkpoint = DeserializeCheckpoint(operation);
        var changed = operation.Status != status
            || operation.ErrorCode != errorCode
            || checkpoint.ArrayId != (arrayId ?? checkpoint.ArrayId)
            || checkpoint.Retryable != retryable
            || checkpoint.ProgressPercentage != progressPercentage;
        checkpoint = checkpoint with
        {
            ArrayId = arrayId ?? checkpoint.ArrayId,
            Retryable = retryable,
            ProgressPercentage = progressPercentage
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
        else if (status == OperationStatus.Running)
        {
            var leaseExpiresAt = timeProvider.GetUtcNow().Add(MaximumOperationLease);
            await database.ResourceLocks
                .Where(resourceLock => resourceLock.OperationId == operationId)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(
                        resourceLock => resourceLock.LeaseExpiresAt,
                        leaseExpiresAt),
                    cancellationToken);
        }
        if (changed)
        {
            AddAuditIntent(
                operation,
                checkpoint.UserId,
                status.ToString().ToLowerInvariant(),
                operation.UpdatedAt.Value);
        }
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
            Subject = "audit.raid.operation",
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

    private static RaidOperation ToApplication(LocalOperation operation)
    {
        var checkpoint = DeserializeCheckpoint(operation);
        return new RaidOperation(
            operation.Id,
            checkpoint.UserId,
            checkpoint.Kind,
            checkpoint.Requested,
            operation.Status,
            operation.ResourceId,
            operation.IdempotencyKey,
            operation.FencingToken,
            checkpoint.ArrayId,
            operation.ErrorCode,
            checkpoint.Retryable,
            checkpoint.ProgressPercentage,
            operation.CreatedAt,
            operation.UpdatedAt);
    }

    private static RaidCheckpoint DeserializeCheckpoint(LocalOperation operation)
    {
        return JsonSerializer.Deserialize<RaidCheckpoint>(operation.Checkpoint ?? "", JsonOptions)
            ?? throw new InvalidOperationException("RAID operation checkpoint is invalid");
    }

    private sealed record RaidCheckpoint(
        Guid UserId,
        RaidOperationKind Kind,
        string? ArrayId,
        RequestedRaidOperation Requested,
        string SnapshotFingerprint,
        bool Retryable,
        int? ProgressPercentage);
}
