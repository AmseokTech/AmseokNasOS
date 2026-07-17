//--------------------------//
//--------配置 SQLite 节点本地数据模型---------//
//--------Configures the SQLite node-local data model--------//
//-------------------------//
using Microsoft.EntityFrameworkCore;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class NodeDbContext(DbContextOptions<NodeDbContext> options) : DbContext(options)
{
    public DbSet<NodeDatabaseState> NodeStates => Set<NodeDatabaseState>();
    public DbSet<LocalOperation> Operations => Set<LocalOperation>();
    public DbSet<ResourceLock> ResourceLocks => Set<ResourceLock>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<NodeDatabaseState>(entity =>
        {
            entity.ToTable("NodeStates");
            entity.HasKey(state => state.NodeId);
            entity.Property(state => state.Version).IsConcurrencyToken();
        });

        builder.Entity<LocalOperation>(entity =>
        {
            entity.ToTable("Operations");
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.Type).HasMaxLength(100);
            entity.Property(operation => operation.ResourceId).HasMaxLength(300);
            entity.Property(operation => operation.IdempotencyKey).HasMaxLength(200);
            entity.Property(operation => operation.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(operation => operation.Checkpoint).HasMaxLength(4_096);
            entity.Property(operation => operation.ErrorCode).HasMaxLength(100);
            entity.HasIndex(operation => new { operation.NodeId, operation.IdempotencyKey }).IsUnique();
            entity.HasIndex(operation => new { operation.Status, operation.CreatedAt });
        });

        builder.Entity<ResourceLock>(entity =>
        {
            entity.ToTable("ResourceLocks");
            entity.HasKey(resourceLock => resourceLock.ResourceId);
            entity.Property(resourceLock => resourceLock.ResourceId).HasMaxLength(300);
            entity.HasIndex(resourceLock => resourceLock.OperationId).IsUnique();
        });

        builder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessages");
            entity.HasKey(message => message.MessageId);
            entity.HasIndex(message => message.ProcessedAt);
        });

        builder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.Subject).HasMaxLength(200);
            entity.Property(message => message.PayloadJson).HasMaxLength(65_536);
            entity.HasIndex(message => new { message.PublishedAt, message.OccurredAt });
        });
    }
}
