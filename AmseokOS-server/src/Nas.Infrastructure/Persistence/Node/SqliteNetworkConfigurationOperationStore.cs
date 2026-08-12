//--------------------------//
//--------持久化网络配置操作、网卡资源锁与审计意图---------//
//--------Persists network configuration operations, interface locks, and audit intents--------//
//-------------------------//
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nas.Application.NetworkConfiguration;
using Nas.Domain.Operations;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class SqliteNetworkConfigurationOperationStore(
    NodeDbContext database,
    TimeProvider timeProvider) : INetworkConfigurationOperationStore
{
    private static readonly Guid LocalNodeId = Guid.Empty;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaximumLease = TimeSpan.FromMinutes(10);

    public async Task<NetworkConfigurationOperationStartOutcome> StartAsync(
        Guid userId,
        string interfaceId,
        NormalizedNetworkConfiguration requested,
        DateTimeOffset confirmationDeadline,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ResourceLocks WHERE LeaseExpiresAt <= {now}",
            cancellationToken);
        var resourceId = $"network-interface:{interfaceId.ToLowerInvariant()}";
        if (await database.ResourceLocks.AnyAsync(
                item => item.ResourceId == resourceId,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new NetworkConfigurationOperationStartRejected("network.resource_locked");
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
        var checkpoint = new NetworkCheckpoint(
            userId,
            interfaceId,
            requested,
            StoredNetworkConfigurationOperationState.Applying,
            confirmationDeadline);
        var operation = new LocalOperation
        {
            Id = operationId,
            NodeId = LocalNodeId,
            Type = "network.configure",
            ResourceId = resourceId,
            IdempotencyKey = $"network:{operationId:D}",
            Status = OperationStatus.Running,
            FencingToken = nodeState.AcceptedFencingToken,
            Checkpoint = JsonSerializer.Serialize(checkpoint, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
        database.Operations.Add(operation);
        database.ResourceLocks.Add(new ResourceLock
        {
            ResourceId = resourceId,
            OperationId = operationId,
            FencingToken = operation.FencingToken,
            LeaseExpiresAt = now.Add(MaximumLease)
        });
        AddAuditIntent(operation, checkpoint, "started", now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new NetworkConfigurationOperationStarted(ToApplication(operation));
    }

    public async Task<StoredNetworkConfigurationOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId && item.Type == "network.configure",
            cancellationToken);
        return operation is null ? null : ToApplication(operation);
    }

    public async Task RecordAsync(
        Guid operationId,
        StoredNetworkConfigurationOperationState state,
        string? errorCode,
        bool releaseLock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var operation = await database.Operations.SingleAsync(
            item => item.Id == operationId && item.Type == "network.configure",
            cancellationToken);
        var checkpoint = DeserializeCheckpoint(operation) with { State = state };
        operation.Status = state switch
        {
            StoredNetworkConfigurationOperationState.Confirmed => OperationStatus.Succeeded,
            StoredNetworkConfigurationOperationState.RolledBack => OperationStatus.Cancelled,
            StoredNetworkConfigurationOperationState.Failed => OperationStatus.Failed,
            StoredNetworkConfigurationOperationState.Interrupted => OperationStatus.Interrupted,
            _ => OperationStatus.Running
        };
        operation.ErrorCode = errorCode;
        operation.Checkpoint = JsonSerializer.Serialize(checkpoint, JsonOptions);
        operation.UpdatedAt = timeProvider.GetUtcNow();
        if (releaseLock)
        {
            await database.ResourceLocks
                .Where(item => item.OperationId == operationId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        AddAuditIntent(
            operation,
            checkpoint,
            state.ToString().ToLowerInvariant(),
            operation.UpdatedAt.Value);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddAuditIntent(
        LocalOperation operation,
        NetworkCheckpoint checkpoint,
        string outcome,
        DateTimeOffset occurredAt)
    {
        database.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            Subject = "audit.network.configuration",
            PayloadJson = JsonSerializer.Serialize(new
            {
                operationId = operation.Id,
                userId = checkpoint.UserId,
                interfaceId = checkpoint.InterfaceId,
                requestedMode = checkpoint.Requested.Mode,
                outcome,
                errorCode = operation.ErrorCode
            }, JsonOptions),
            OccurredAt = occurredAt
        });
    }

    private static StoredNetworkConfigurationOperation ToApplication(LocalOperation operation)
    {
        var checkpoint = DeserializeCheckpoint(operation);
        return new StoredNetworkConfigurationOperation(
            operation.Id,
            checkpoint.UserId,
            checkpoint.InterfaceId,
            checkpoint.Requested,
            checkpoint.State,
            checkpoint.ConfirmationDeadline,
            operation.ErrorCode);
    }

    private static NetworkCheckpoint DeserializeCheckpoint(LocalOperation operation) =>
        JsonSerializer.Deserialize<NetworkCheckpoint>(operation.Checkpoint ?? "", JsonOptions)
        ?? throw new InvalidOperationException("Network operation checkpoint is invalid");

    private sealed record NetworkCheckpoint(
        Guid UserId,
        string InterfaceId,
        NormalizedNetworkConfiguration Requested,
        StoredNetworkConfigurationOperationState State,
        DateTimeOffset ConfirmationDeadline);
}
