using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nas.Infrastructure;
using Nas.Infrastructure.ClusterServices;
using Nas.Infrastructure.Persistence.Cluster;
using Nas.Infrastructure.Persistence.Node;

//--------------------------//
//--------API 入口只装配控制面 HTTP 管道---------//
//--------The API entry point only composes the control-plane HTTP pipeline--------//
//-------------------------//
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddNasInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ClusterDbContext>("postgresql", tags: ["ready"])
    .AddDbContextCheck<NodeDbContext>("sqlite", tags: ["ready"])
    .AddCheck<EtcdHealthCheck>("etcd", tags: ["ready"])
    .AddCheck<NatsHealthCheck>("nats", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();
