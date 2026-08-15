//--------------------------//
//--------鉴权并转换应用市场只读目录查询---------//
//--------Authorizes and translates read-only app-catalog queries--------//
//-------------------------//
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nas.Api.Contracts;
using Nas.Application.AppStore;
using Nas.Application.Authentication;

namespace Nas.Api.Controllers;

[ApiController]
[Route("api/app-store")]
public sealed class AppStoreController(
    IAppCatalogService catalogService,
    ILogger<AppStoreController> logger) : ControllerBase
{
    [HttpGet("catalog")]
    [Authorize(Policy = AuthenticationDefaults.PasswordChangedPolicy)]
    [ProducesResponseType<AppCatalogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AppCatalogResponse>> GetCatalog(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await catalogService.GetCatalogAsync(cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Ok(AppCatalogResponse.From(snapshot));
        }
        catch (AppCatalogUnavailableException exception)
        {
            logger.LogWarning(
                exception.InnerException,
                "Application catalog query failed with {ErrorCode}",
                exception.Code);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "应用市场暂不可用",
                detail: PublicErrorMessage(exception.Code),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = exception.Code,
                    ["retryable"] = true
                });
        }
    }

    private static string PublicErrorMessage(string code)
    {
        return code switch
        {
            "app_catalog.disabled" => "此 NAS 尚未启用远端应用市场",
            "app_catalog.invalid" or "app_catalog.invalid_not_modified" =>
                "远端应用目录未通过安全校验",
            _ => "暂时无法连接远端应用市场，请稍后重试"
        };
    }
}
