//--------------------------//
//--------为 SQLite 迁移提供无运行时依赖的设计入口---------//
//--------Provides a runtime-independent design entry for SQLite migrations--------//
//-------------------------//
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nas.Infrastructure.Persistence.Node;

public sealed class NodeDbContextFactory : IDesignTimeDbContextFactory<NodeDbContext>
{
    public NodeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NodeDbContext>()
            .UseSqlite("Data Source=amseoknas-node.design.db")
            .Options;

        return new NodeDbContext(options);
    }
}
