//--------------------------//
//--------检查 etcd 与 NATS 的只读健康端点---------//
//--------Checks read-only etcd and NATS health endpoints--------//
//-------------------------//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Nas.Infrastructure.ClusterServices;

public sealed class EtcdHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<ClusterServicesOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        ClusterServiceHealthCheck.CheckAsync(
            httpClientFactory,
            options.Value.EtcdHealthUrl,
            cancellationToken);
}

public sealed class NatsHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<ClusterServicesOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        ClusterServiceHealthCheck.CheckAsync(
            httpClientFactory,
            options.Value.NatsHealthUrl,
            cancellationToken);
}

internal static class ClusterServiceHealthCheck
{
    public static async Task<HealthCheckResult> CheckAsync(
        IHttpClientFactory httpClientFactory,
        Uri healthUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient("cluster-health")
                .GetAsync(healthUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Endpoint returned HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Unhealthy("Endpoint is unreachable", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Endpoint health request timed out", exception);
        }
    }
}
