//--------------------------//
//--------按显式配置迁移双数据库并加固 SQLite 文件---------//
//--------Migrates both databases and hardens the SQLite file when enabled--------//
//-------------------------//
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nas.Infrastructure.Persistence.Cluster;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Infrastructure.Persistence;

public sealed class DatabaseMigrationService(
    IServiceScopeFactory scopeFactory,
    IOptions<PersistenceOptions> options,
    ILogger<DatabaseMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Automatic database migrations are disabled");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var clusterDatabase = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();
        var nodeDatabase = scope.ServiceProvider.GetRequiredService<NodeDbContext>();

        logger.LogInformation("Applying PostgreSQL cluster database migrations");
        await clusterDatabase.Database.MigrateAsync(cancellationToken);

        var sqlitePath = GetSqlitePath(nodeDatabase);
        EnsureSqliteDirectory(sqlitePath);

        logger.LogInformation("Applying SQLite node database migrations");
        await nodeDatabase.Database.MigrateAsync(cancellationToken);
        await ConfigureAndCheckSqliteAsync(nodeDatabase, cancellationToken);
        RestrictSqliteFile(sqlitePath);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetSqlitePath(NodeDbContext database)
    {
        var connectionString = database.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Node database connection string is missing");
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        {
            throw new InvalidOperationException("Node database must use a persistent SQLite file");
        }

        return Path.GetFullPath(dataSource);
    }

    private static void EnsureSqliteDirectory(string sqlitePath)
    {
        var directory = Path.GetDirectoryName(sqlitePath)
            ?? throw new InvalidOperationException("Node database directory is invalid");

        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static async Task ConfigureAndCheckSqliteAsync(
        NodeDbContext database,
        CancellationToken cancellationToken)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (!string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite quick_check did not return ok");
            }
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }

    private static void RestrictSqliteFile(string sqlitePath)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                sqlitePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
