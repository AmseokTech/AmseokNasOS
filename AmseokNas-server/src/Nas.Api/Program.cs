using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.RateLimiting;
using Nas.Infrastructure;
using Nas.Application.Authentication;
using Nas.Infrastructure.ClusterServices;
using Nas.Infrastructure.Persistence.Cluster;
using Nas.Infrastructure.Persistence.Node;

//--------------------------//
//--------API 入口只装配控制面 HTTP 管道---------//
//--------The API entry point only composes the control-plane HTTP pipeline--------//
//-------------------------//
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization(options =>
{
    var passwordChangeSessionPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    var passwordChangedPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(AuthenticationDefaults.MustChangePasswordClaim, "false")
        .Build();

    options.DefaultPolicy = passwordChangedPolicy;
    options.FallbackPolicy = passwordChangedPolicy;
    options.AddPolicy(
        AuthenticationDefaults.PasswordChangedPolicy,
        passwordChangedPolicy);
    options.AddPolicy(
        AuthenticationDefaults.PasswordChangeSessionPolicy,
        passwordChangeSessionPolicy);
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AmseokNas.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            }));
});
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddNasInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ClusterDbContext>("postgresql", tags: ["ready"])
    .AddDbContextCheck<NodeDbContext>("sqlite", tags: ["ready"])
    .AddCheck<EtcdHealthCheck>("etcd", tags: ["ready"])
    .AddCheck<NatsHealthCheck>("nats", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();
