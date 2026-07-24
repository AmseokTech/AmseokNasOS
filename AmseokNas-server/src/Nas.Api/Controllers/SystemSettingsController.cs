//--------------------------//
//--------鉴权并转换关于本机与网络只读查询---------//
//--------Authorizes and translates read-only About and Network queries--------//
//-------------------------//
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.SystemSettings;

namespace Nas.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SystemSettingsController(
    ISystemSettingsService settings,
    ILogger<SystemSettingsController> logger) : ControllerBase
{
    [HttpGet("system/about")]
    [Authorize(Policy = AuthenticationDefaults.SystemReadPolicy)]
    [ProducesResponseType<SystemAboutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SystemAboutResponse>> GetAbout(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(SystemAboutResponse.From(
                await settings.GetAboutAsync(cancellationToken)));
        }
        catch (PrivilegedClientException exception)
        {
            return PrivilegedUnavailable(exception);
        }
    }

    [HttpGet("network/interfaces")]
    [Authorize(Policy = AuthenticationDefaults.NetworkReadPolicy)]
    [ProducesResponseType<IReadOnlyList<NetworkInterfaceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<NetworkInterfaceResponse>>> GetNetworkInterfaces(
        CancellationToken cancellationToken)
    {
        try
        {
            var interfaces = await settings.GetNetworkInterfacesAsync(cancellationToken);
            return Ok(interfaces.Select(NetworkInterfaceResponse.From).ToArray());
        }
        catch (PrivilegedClientException exception)
        {
            return PrivilegedUnavailable(exception);
        }
    }

    private ObjectResult PrivilegedUnavailable(PrivilegedClientException exception)
    {
        logger.LogWarning(
            exception.InnerException,
            "Privileged system query failed with {ErrorCode}; retryable: {Retryable}; diagnostic: {DiagnosticMessage}",
            exception.Code,
            exception.Retryable,
            exception.DiagnosticMessage);
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "系统信息暂不可用",
            detail: PublicErrorMessage(exception.Code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.Code,
                ["retryable"] = exception.Retryable
            });
    }

    private static string PublicErrorMessage(string code)
    {
        return code switch
        {
            "privileged.disabled" => "底层系统查询服务尚未启用",
            "privileged.unavailable" => "底层系统查询服务当前不可用",
            "protocol.unsupported_version" => "底层系统查询服务协议不兼容",
            "request.deadline_exceeded" => "底层系统查询已超时",
            "inventory.read_failed" => "底层系统信息读取失败",
            _ => "底层系统查询未能完成"
        };
    }
}
