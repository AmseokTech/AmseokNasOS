//--------------------------//
//--------鉴权并映射 Web Terminal HTTP 与 WebSocket 边界---------//
//--------Authorizes and maps Web Terminal HTTP and WebSocket boundaries--------//
//-------------------------//
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Nas.Api.Contracts;
using Nas.Api.Terminal;
using Nas.Application.Authentication;
using Nas.Application.Terminal;

namespace Nas.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationDefaults.TerminalAccessPolicy)]
[Route("api/terminal")]
public sealed class TerminalController(
    ITerminalSessionService terminalSessions,
    ITerminalBrokerClient broker,
    ITerminalWebSocketRelay relay,
    IOptions<TerminalOptions> options,
    ILogger<TerminalController> logger) : ControllerBase
{
    private const string WebSocketSubProtocol = "amseoknas-terminal.v1";

    [HttpPost("sessions")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("terminal-session")]
    public async Task<ActionResult<CreateTerminalSessionResponse>> CreateSession(
        CreateTerminalSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var outcome = await terminalSessions.CreateAsync(
            userId.Value,
            request.Password,
            request.Columns,
            request.Rows,
            cancellationToken);

        if (outcome is TerminalSessionCreationRejected
            {
                Failure: TerminalSessionCreationFailure.Disabled
            })
        {
            return TerminalUnavailable("Web Terminal 当前未启用");
        }

        if (outcome is TerminalSessionCreationRejected
            {
                Failure: TerminalSessionCreationFailure.ReauthenticationFailed
            })
        {
            logger.LogWarning(
                "Terminal session reauthentication failed for user {UserId}",
                userId);
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "重新认证失败",
                detail: "管理员密码不正确",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "TerminalReauthenticationFailed"
                });
        }

        var session = outcome is TerminalSessionCreated created
            ? created.Session
            : throw new InvalidOperationException(
                "Terminal session service returned an unknown outcome");
        logger.LogInformation(
            "Terminal session {SessionId} was authorized for user {UserId} from {RemoteAddress}",
            session.Id,
            userId,
            HttpContext.Connection.RemoteIpAddress);
        return Created(
            $"/api/terminal/sessions/{session.Id}",
            new CreateTerminalSessionResponse(
                session.Id,
                session.ExpiresAt,
                $"/api/terminal/sessions/{session.Id}/socket"));
    }

    [HttpGet("sessions/{sessionId:guid}/socket")]
    public async Task<IActionResult> Connect(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!terminalSessions.IsEnabled)
        {
            return TerminalUnavailable("Web Terminal 当前未启用");
        }
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return BadRequest(new { code = "WebSocketUpgradeRequired" });
        }
        if (!IsAllowedOrigin(Request.Headers.Origin.ToString()))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "终端连接来源被拒绝",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "TerminalOriginRejected"
                });
        }
        if (!HttpContext.WebSockets.WebSocketRequestedProtocols.Contains(
                WebSocketSubProtocol,
                StringComparer.Ordinal))
        {
            return BadRequest(new { code = "TerminalProtocolRequired" });
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var consumption = terminalSessions.Consume(sessionId, userId.Value);
        if (consumption is TerminalSessionConsumptionRejected
            {
                Failure: TerminalSessionConsumptionFailure.Disabled
            })
        {
            return TerminalUnavailable("Web Terminal 当前未启用");
        }
        if (consumption is TerminalSessionConsumptionRejected
            {
                Failure: TerminalSessionConsumptionFailure.Unavailable
            })
        {
            return Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "终端会话已过期或已使用",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "TerminalSessionUnavailable"
                });
        }

        var pending = consumption is TerminalSessionConsumed consumed
            ? consumed.Session
            : throw new InvalidOperationException(
                "Terminal session service returned an unknown outcome");
        ITerminalBrokerSession brokerSession;
        try
        {
            brokerSession = await broker.OpenAsync(
                pending.Id,
                pending.Columns,
                pending.Rows,
                cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            logger.LogError(
                exception,
                "Terminal broker was unavailable for session {SessionId}",
                sessionId);
            return TerminalUnavailable("低权限终端服务当前不可用");
        }

        await using (brokerSession)
        using (var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(WebSocketSubProtocol))
        {
            await relay.RelayAsync(
                webSocket,
                brokerSession,
                pending.Id,
                cancellationToken);
        }

        return new EmptyResult();
    }

    private bool IsAllowedOrigin(string origin)
    {
        return options.Value.AllowedOrigins.Any(
            allowed => string.Equals(
                allowed.TrimEnd('/'),
                origin.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));
    }

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private ObjectResult TerminalUnavailable(string detail)
    {
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "终端不可用",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "TerminalUnavailable"
            });
    }
}
