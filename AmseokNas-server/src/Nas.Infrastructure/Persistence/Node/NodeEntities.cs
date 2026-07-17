//--------------------------//
//--------定义 SQLite 节点执行、锁和消息记录---------//
//--------Defines SQLite node execution, lock, and message records--------//
//-------------------------//
using Nas.Domain.Operations;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class NodeDatabaseState
{
    public Guid NodeId { get; set; }
    public Guid ClusterId { get; set; }
    public long AcceptedFencingToken { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LocalOperation
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public required string Type { get; set; }
    public required string ResourceId { get; set; }
    public required string IdempotencyKey { get; set; }
    public OperationStatus Status { get; set; }
    public long FencingToken { get; set; }
    public string? Checkpoint { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class ResourceLock
{
    public required string ResourceId { get; set; }
    public Guid OperationId { get; set; }
    public long FencingToken { get; set; }
    public DateTimeOffset LeaseExpiresAt { get; set; }
}

public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public Guid? OperationId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class OutboxMessage
{
    public Guid MessageId { get; set; }
    public required string Subject { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int PublishAttempts { get; set; }
}
