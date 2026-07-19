//--------------------------//
//--------集中装配基础设施依赖与安全配置---------//
//--------Composes infrastructure dependencies and secure configuration--------//
//-------------------------//
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nas.Application.Authentication;
using Nas.Infrastructure.Authentication;
using Nas.Infrastructure.ClusterServices;
using Nas.Infrastructure.Persistence;
using Nas.Infrastructure.Persistence.Cluster;
using Nas.Infrastructure.Persistence.Node;

namespace Nas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNasInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
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
            .AddSignInManager()
            .AddRoles<NasRole>()
            .AddEntityFrameworkStores<ClusterDbContext>();

        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        });
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                options.Cookie.Name = "AmseokNas.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddScoped<IAuthenticationService, IdentityAuthenticationService>();
        services.AddScoped<IUserClaimsPrincipalFactory<NasUser>, NasUserClaimsPrincipalFactory>();

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
