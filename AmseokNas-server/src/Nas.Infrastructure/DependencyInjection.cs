//--------------------------//
//--------集中装配基础设施依赖与安全配置---------//
//--------Composes infrastructure dependencies and secure configuration--------//
//-------------------------//
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nas.Infrastructure.ClusterServices;
using Nas.Infrastructure.Persistence;
using Nas.Infrastructure.Persistence.Cluster;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNasInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var clusterConnection = RequireConnectionString(configuration, "ClusterDatabase");
        var nodeConnection = RequireConnectionString(configuration, "NodeDatabase");

        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName));
        services.AddOptions<ClusterServicesOptions>()
            .Bind(configuration.GetSection(ClusterServicesOptions.SectionName))
            .Validate(
                options => options.EtcdHealthUrl is not null && options.NatsHealthUrl is not null,
                "Both etcd and NATS health URLs are required")
            .ValidateOnStart();

        services.AddDbContext<ClusterDbContext>(options => options.UseNpgsql(clusterConnection));
        services.AddDbContext<NodeDbContext>(options => options.UseSqlite(nodeConnection));

        services.AddIdentityCore<NasUser>()
            .AddRoles<NasRole>()
            .AddEntityFrameworkStores<ClusterDbContext>();

        services.AddHttpClient("cluster-health", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddHostedService<DatabaseMigrationService>();

        return services;
    }

    private static string RequireConnectionString(
        IConfiguration configuration,
        string name)
    {
        var connectionString = configuration.GetConnectionString(name);

        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException($"Connection string '{name}' is required");
    }
}
