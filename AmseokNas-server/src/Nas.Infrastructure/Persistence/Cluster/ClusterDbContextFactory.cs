//--------------------------//
//--------为 PostgreSQL 迁移提供无运行时依赖的设计入口---------//
//--------Provides a runtime-independent design entry for PostgreSQL migrations--------//
//-------------------------//
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nas.Infrastructure.Persistence.Cluster;

public sealed class ClusterDbContextFactory : IDesignTimeDbContextFactory<ClusterDbContext>
{
    public ClusterDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseNpgsql("Host=localhost;Database=amseoknas;Username=amseoknas")
            .Options;

        return new ClusterDbContext(options);
    }
}
