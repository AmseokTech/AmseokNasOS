//--------------------------//
//--------暴露管理员登录、会话查询与强制改密 HTTP 边界---------//
//--------Exposes HTTP boundaries for administrator sign-in, sessions, and forced password changes--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nas.Api.Contracts;
using Nas.Application.Authentication;

namespace Nas.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(
    IAuthenticationService authentication,
    IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery request token is missing"),
            new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                Path = "/"
            });
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await authentication.SignInAdministratorAsync(
            request.Password,
            cancellationToken);

        if (outcome.Succeeded)
        {
            return Ok(new AuthenticationResponse(
                AuthenticationDefaults.AdministratorUserName,
                outcome.MustChangePassword));
        }

        if (outcome.Failure == SignInFailure.LockedOut)
        {
            return Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "账户已暂时锁定",
                detail: "登录失败次数过多，请稍后再试",
                extensions: ProblemExtensions("AccountLocked"));
        }

        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "登录失败",
            detail: "密码错误或账户当前不可登录",
            extensions: ProblemExtensions("InvalidCredentials"));
    }

    [Authorize(Policy = AuthenticationDefaults.PasswordChangeSessionPolicy)]
    [HttpGet("session")]
    public async Task<ActionResult<AuthenticationResponse>> GetSession(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await authentication.GetUserAsync(userId.Value, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(new AuthenticationResponse(user.UserName, user.MustChangePassword));
    }

    [Authorize(Policy = AuthenticationDefaults.PasswordChangeSessionPolicy)]
    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var outcome = await authentication.ChangePasswordAsync(
            userId.Value,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (outcome.Succeeded)
        {
            return NoContent();
        }

        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "密码修改失败",
            detail: "请确认当前密码正确，且新密码满足复杂度要求",
            extensions: ProblemExtensions("PasswordChangeRejected", outcome.ErrorCodes));
    }

    [Authorize(Policy = AuthenticationDefaults.PasswordChangeSessionPolicy)]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authentication.SignOutAsync(cancellationToken);
        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static Dictionary<string, object?> ProblemExtensions(
        string code,
        IReadOnlyCollection<string>? identityErrors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code
        };

        if (identityErrors is { Count: > 0 })
        {
            extensions["identityErrors"] = identityErrors;
        }

        return extensions;
    }
}
