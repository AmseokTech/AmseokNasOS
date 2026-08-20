//--------------------------//
//--------验证系统设置只读 API 的鉴权与响应映射---------//
//--------Verifies authorization and response mapping for settings read APIs--------//
//-------------------------//
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Nas.Api.Contracts;
using Nas.Api.Controllers;
using Nas.Application.Authentication;
using Nas.Application.Privileged;
using Nas.Application.SystemSettings;

namespace Nas.Api.Tests;

public sealed class SystemSettingsControllerTests
{
    [Fact]
    public async Task GetAboutMapsTheServiceResult()
    {
        var service = new SystemSettingsServiceStub();
        var controller = new SystemSettingsController(
            service,
            NullLogger<SystemSettingsController>.Instance);

        var result = await controller.GetAbout(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SystemAboutResponse>(ok.Value);
        Assert.Equal(service.About.HostName, response.HostName);
        Assert.Equal(service.About.SystemStorage.StableId, response.SystemStorage.StableId);
    }

    [Fact]
    public async Task GetPerformanceMapsTheServiceResult()
    {
        var service = new SystemSettingsServiceStub();
        var controller = new SystemSettingsController(
            service,
            NullLogger<SystemSettingsController>.Instance);

        var result = await controller.GetPerformance(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SystemPerformanceResponse>(ok.Value);
        Assert.Equal(service.Performance.Cpu.Model, response.Cpu.Model);
        Assert.Equal(service.Performance.Memory.UsedBytes, response.Memory.UsedBytes);
    }

    [Fact]
    public void ReadEndpointsRequireTheirSpecificPolicies()
    {
        var aboutPolicy = PolicyFor(nameof(SystemSettingsController.GetAbout));
        var performancePolicy = PolicyFor(nameof(SystemSettingsController.GetPerformance));
        var networkPolicy = PolicyFor(nameof(SystemSettingsController.GetNetworkInterfaces));

        Assert.Equal(AuthenticationDefaults.SystemReadPolicy, aboutPolicy);
        Assert.Equal(AuthenticationDefaults.SystemReadPolicy, performancePolicy);
        Assert.Equal(AuthenticationDefaults.NetworkReadPolicy, networkPolicy);
    }

    [Fact]
    public async Task PrivilegedFailureIsReturnedAsAServiceUnavailableProblem()
    {
        var service = new SystemSettingsServiceStub
        {
            Error = new PrivilegedClientException(
                "privileged.unavailable",
                "/etc/amseoknas/internal-secret could not be read",
                true,
                diagnosticMessage: "internal-only diagnostic")
        };
        var controller = new SystemSettingsController(
            service,
            NullLogger<SystemSettingsController>.Instance);

        var result = await controller.GetNetworkInterfaces(CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, unavailable.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(unavailable.Value);
        Assert.Equal("privileged.unavailable", problem.Extensions["code"]);
        Assert.Equal(true, problem.Extensions["retryable"]);
        Assert.Equal("底层系统查询服务当前不可用", problem.Detail);
        Assert.DoesNotContain("/etc/amseoknas", problem.Detail);
    }

    private static string? PolicyFor(string methodName)
    {
        return typeof(SystemSettingsController)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)?
            .GetCustomAttribute<AuthorizeAttribute>()?
            .Policy;
    }

    private sealed class SystemSettingsServiceStub : ISystemSettingsService
    {
        public SystemAboutInformation About { get; } = new(
            "nas-test",
            "AmseokOS",
            "6.12.0",
            3600,
            new CpuInformation("Test CPU", 4, 8, 2400, 4200),
            new MemoryInformation(32L * 1024 * 1024 * 1024),
            new SystemStorageInformation(
                "/dev/test",
                "test-disk",
                "Test Disk",
                1024,
                512,
                512));

        public PrivilegedClientException? Error { get; init; }

        public SystemPerformanceInformation Performance { get; } = new(
            1_700_000_000_000,
            new CpuPerformanceInformation(
                "Test CPU",
                4,
                8,
                2400,
                4200,
                256 * 1024,
                4 * 1024 * 1024,
                16 * 1024 * 1024,
                new CpuTimeCounterInformation("cpu", 1000, 250),
                [new CpuTimeCounterInformation("cpu0", 125, 25)]),
            new MemoryPerformanceInformation(1024, 512, 512, 128, 256, 64),
            [],
            [],
            []);

        public Task<SystemAboutInformation> GetAboutAsync(
            CancellationToken cancellationToken)
        {
            return Error is null
                ? Task.FromResult(About)
                : Task.FromException<SystemAboutInformation>(Error);
        }

        public Task<SystemPerformanceInformation> GetPerformanceAsync(
            CancellationToken cancellationToken)
        {
            return Error is null
                ? Task.FromResult(Performance)
                : Task.FromException<SystemPerformanceInformation>(Error);
        }

        public Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
            CancellationToken cancellationToken)
        {
            return Error is null
                ? Task.FromResult<IReadOnlyList<NetworkInterfaceInformation>>([])
                : Task.FromException<IReadOnlyList<NetworkInterfaceInformation>>(Error);
        }
    }
}
