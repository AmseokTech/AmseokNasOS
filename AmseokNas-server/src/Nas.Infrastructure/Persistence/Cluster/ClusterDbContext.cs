//--------------------------//
//--------配置 PostgreSQL 全局数据与 Identity 模型---------//
//--------Configures PostgreSQL global data and Identity models--------//
//-------------------------//
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nas.Domain.Operations;
using Nas.Domain.Permissions;

namespace Nas.Infrastructure.Persistence.Cluster;

public sealed class ClusterDbContext(DbContextOptions<ClusterDbContext> options)
    : IdentityDbContext<NasUser, NasRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<ClusterRecord> Clusters => Set<ClusterRecord>();
    public DbSet<NodeRegistration> Nodes => Set<NodeRegistration>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<GlobalOperation> Operations => Set<GlobalOperation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentity(builder);
        ConfigureCluster(builder);
        ConfigureOperations(builder);
        ConfigureAudit(builder);
        SeedPermissions(builder);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<NasUser>(entity =>
        {
            entity.Property(user => user.SecurityVersion).IsConcurrencyToken();
            entity.Property(user => user.CreatedAt).IsRequired();
        });

        builder.Entity<PermissionRecord>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasKey(permission => permission.Code);
            entity.Property(permission => permission.Code).HasMaxLength(100);
            entity.Property(permission => permission.Description).HasMaxLength(200);
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(item => new { item.RoleId, item.PermissionCode });
            entity.Property(item => item.PermissionCode).HasMaxLength(100);
            entity.HasOne<NasRole>()
                .WithMany()
                .HasForeignKey(item => item.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PermissionRecord>()
                .WithMany()
                .HasForeignKey(item => item.PermissionCode)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCluster(ModelBuilder builder)
    {
        builder.Entity<ClusterRecord>(entity =>
        {
            entity.ToTable("Clusters");
            entity.HasKey(cluster => cluster.Id);
            entity.Property(cluster => cluster.Name).HasMaxLength(120);
            entity.Property(cluster => cluster.Version).IsConcurrencyToken();
        });

        builder.Entity<NodeRegistration>(entity =>
        {
            entity.ToTable("Nodes");
            entity.HasKey(node => node.Id);
            entity.Property(node => node.DisplayName).HasMaxLength(120);
            entity.Property(node => node.Version).IsConcurrencyToken();
            entity.HasIndex(node => new { node.ClusterId, node.DisplayName }).IsUnique();
            entity.HasOne<ClusterRecord>()
                .WithMany()
                .HasForeignKey(node => node.ClusterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOperations(ModelBuilder builder)
    {
        builder.Entity<GlobalOperation>(entity =>
        {
            entity.ToTable("Operations");
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.Type).HasMaxLength(100);
            entity.Property(operation => operation.ResourceId).HasMaxLength(300);
            entity.Property(operation => operation.IdempotencyKey).HasMaxLength(200);
            entity.Property(operation => operation.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(operation => operation.Version).IsConcurrencyToken();
            entity.HasIndex(operation => new { operation.ClusterId, operation.IdempotencyKey }).IsUnique();
            entity.HasIndex(operation => new { operation.NodeId, operation.Status, operation.CreatedAt });
            entity.HasOne<ClusterRecord>()
                .WithMany()
                .HasForeignKey(operation => operation.ClusterId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NodeRegistration>()
                .WithMany()
                .HasForeignKey(operation => operation.NodeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NasUser>()
                .WithMany()
                .HasForeignKey(operation => operation.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureAudit(ModelBuilder builder)
    {
        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.EventType).HasMaxLength(150);
            entity.Property(audit => audit.Outcome).HasMaxLength(50);
            entity.Property(audit => audit.CorrelationId).HasMaxLength(100);
            entity.HasIndex(audit => new { audit.ClusterId, audit.CreatedAt });
        });
    }

    private static void SeedPermissions(ModelBuilder builder)
    {
        builder.Entity<PermissionRecord>().HasData(
            SystemPermissions.All.Select(code => new PermissionRecord
            {
                Code = code,
                Description = code
            }));
    }
}
