//--------------------------//
//--------定义 PostgreSQL 全局身份与控制面记录---------//
//--------Defines PostgreSQL global identity and control-plane records--------//
//-------------------------//
using Microsoft.AspNetCore.Identity;
using Nas.Domain.Operations;

namespace Nas.Infrastructure.Persistence.Cluster;

public sealed class NasUser : IdentityUser<Guid>
{
    public long SecurityVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NasRole : IdentityRole<Guid>
{
}

public sealed class ClusterRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NodeRegistration
{
    public Guid Id { get; set; }
    public Guid ClusterId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsControlEligible { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class PermissionRecord
{
    public required string Code { get; set; }
    public required string Description { get; set; }
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public required string PermissionCode { get; set; }
}

public sealed class GlobalOperation
{
    public Guid Id { get; set; }
    public Guid ClusterId { get; set; }
    public Guid NodeId { get; set; }
    public Guid? UserId { get; set; }
    public required string Type { get; set; }
    public required string ResourceId { get; set; }
    public required string IdempotencyKey { get; set; }
    public OperationStatus Status { get; set; }
    public long FencingToken { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid ClusterId { get; set; }
    public Guid? NodeId { get; set; }
    public Guid? UserId { get; set; }
    public required string EventType { get; set; }
    public required string Outcome { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
