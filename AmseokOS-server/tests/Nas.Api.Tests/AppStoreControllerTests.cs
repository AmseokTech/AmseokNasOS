//--------------------------//
//--------验证应用市场只读 API 的鉴权与错误映射---------//
//--------Verifies authorization and error mapping for the app-store API--------//
//-------------------------//
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Nas.Api.Contracts;
using Nas.Api.Controllers;
using Nas.Application.AppStore;
using Nas.Application.Authentication;

namespace Nas.Api.Tests;

public sealed class AppStoreControllerTests
{
    [Fact]
    public async Task CatalogResponsePreservesCacheStateAndUsesNoStore()
    {
        var controller = CreateController(new CatalogServiceStub());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.GetCatalog(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AppCatalogResponse>(ok.Value);
        Assert.Equal("revision-1", response.Revision);
        Assert.True(response.IsStale);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public void CatalogRequiresAPasswordChangedSession()
    {
        var policy = typeof(AppStoreController)
            .GetMethod(nameof(AppStoreController.GetCatalog), BindingFlags.Instance | BindingFlags.Public)?
            .GetCustomAttribute<AuthorizeAttribute>()?
            .Policy;

        Assert.Equal(AuthenticationDefaults.PasswordChangedPolicy, policy);
    }

    [Fact]
    public async Task InvalidCatalogReturnsASanitizedServiceUnavailableProblem()
    {
        var controller = CreateController(new CatalogServiceStub
        {
            Error = new AppCatalogUnavailableException(
                "app_catalog.invalid",
                "https://internal.example/secret.json is invalid")
        });

        var result = await controller.GetCatalog(CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, unavailable.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(unavailable.Value);
        Assert.Equal("远端应用目录未通过安全校验", problem.Detail);
        Assert.DoesNotContain("internal.example", problem.Detail);
    }

    private static AppStoreController CreateController(IAppCatalogService service)
    {
        return new AppStoreController(
            service,
            NullLogger<AppStoreController>.Instance);
    }

    private sealed class CatalogServiceStub : IAppCatalogService
    {
        public AppCatalogUnavailableException? Error { get; init; }

        public Task<AppCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken)
        {
            if (Error is not null)
            {
                return Task.FromException<AppCatalogSnapshot>(Error);
            }

            return Task.FromResult(new AppCatalogSnapshot(
                new AppCatalog(
                    "amseok-app-catalog-v1",
                    "revision-1",
                    DateTimeOffset.UnixEpoch,
                    []),
                DateTimeOffset.UnixEpoch,
                true));
        }
    }
}
