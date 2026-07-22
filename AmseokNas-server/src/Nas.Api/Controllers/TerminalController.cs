//--------------------------//
//--------鉴权并转发有界 Web Terminal 会话---------//
//--------Authorizes and relays bounded Web Terminal sessions--------//
//-------------------------//
using System.Buffers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.Terminal;

namespace Nas.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationDefaults.TerminalAccessPolicy)]
[Route("api/terminal")]
public sealed class TerminalController(
    IAuthenticationService authentication,
    ITerminalSessionStore sessions,
    ITerminalBrokerClient broker,
    IOptions<TerminalOptions> options,
    ILogger<TerminalController> logger) : ControllerBase
{
    private const string WebSocketSubProtocol = "amseoknas-terminal.v1";
    private const int MaximumInputMessageBytes = 64 * 1024;
    private const int MaximumControlMessageBytes = 4 * 1024;

    [HttpPost("sessions")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("terminal-session")]
    public async Task<ActionResult<CreateTerminalSessionResponse>> CreateSession(
        CreateTerminalSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return TerminalUnavailable("Web Terminal 当前未启用");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await authentication.VerifyPasswordAsync(
                userId.Value,
                request.Password,
                cancellationToken))
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

        var session = sessions.Create(userId.Value, request.Columns, request.Rows);
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
        if (!options.Value.Enabled)
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

        var pending = sessions.Consume(sessionId, userId.Value);
        if (pending is null)
        {
            return Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "终端会话已过期或已使用",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "TerminalSessionUnavailable"
                });
        }

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
            await RelaySessionAsync(webSocket, brokerSession, pending, cancellationToken);
        }

        return new EmptyResult();
    }

    private async Task RelaySessionAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        PendingTerminalSession session,
        CancellationToken requestCancellationToken)
    {
        using var absoluteTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.MaximumSessionMinutes));
        using var idleTimeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes));
        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            absoluteTimeout.Token,
            idleTimeout.Token);
        long inputBytes = 0;
        long outputBytes = 0;

        void ReportActivity() => idleTimeout.CancelAfter(
            TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes));

        var inputTask = RelayBrowserInputAsync(
            webSocket,
            brokerSession,
            ReportActivity,
            count => Interlocked.Add(ref inputBytes, count),
            relayCancellation.Token);
        var outputTask = RelayBrokerOutputAsync(
            brokerSession,
            webSocket,
            ReportActivity,
            count => Interlocked.Add(ref outputBytes, count),
            relayCancellation.Token);

        string outcome;
        try
        {
            await Task.WhenAny(inputTask, outputTask);
            relayCancellation.Cancel();
            await brokerSession.CloseAsync(CancellationToken.None);
            await ObserveRelayTaskAsync(inputTask);
            await ObserveRelayTaskAsync(outputTask);
            outcome = absoluteTimeout.IsCancellationRequested
                ? "MaximumDuration"
                : idleTimeout.IsCancellationRequested
                    ? "IdleTimeout"
                    : "Closed";
        }
        catch (Exception exception)
        {
            outcome = "Failed";
            logger.LogWarning(
                exception,
                "Terminal session {SessionId} relay failed",
                session.Id);
        }

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Terminal session ended",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The browser may already have disconnected.
            }
        }

        logger.LogInformation(
            "Terminal session {SessionId} ended with {Outcome}; input {InputBytes} bytes, output {OutputBytes} bytes",
            session.Id,
            outcome,
            inputBytes,
            outputBytes);
    }

    private static async Task RelayBrowserInputAsync(
        WebSocket webSocket,
        ITerminalBrokerSession brokerSession,
        Action reportActivity,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    var limit = result.MessageType == WebSocketMessageType.Binary
                        ? MaximumInputMessageBytes
                        : MaximumControlMessageBytes;
                    if (message.Length + result.Count > limit)
                    {
                        throw new InvalidDataException("Terminal WebSocket message exceeds its limit");
                    }
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                reportActivity();
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var data = message.ToArray();
                    if (data.Length > 0)
                    {
                        await brokerSession.SendInputAsync(data, cancellationToken);
                        reportBytes(data.Length);
                    }
                    continue;
                }

                var control = JsonSerializer.Deserialize(
                    message.ToArray(),
                    TerminalApiJsonContext.Default.TerminalClientControlMessage);
                switch (control)
                {
                    case TerminalResizeMessage resize
                        when resize.Columns is >= 20 and <= 300
                            && resize.Rows is >= 5 and <= 120:
                        await brokerSession.ResizeAsync(
                            resize.Columns,
                            resize.Rows,
                            cancellationToken);
                        break;
                    case TerminalCloseMessage:
                        return;
                    default:
                        throw new InvalidDataException("Terminal control message is invalid");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task RelayBrokerOutputAsync(
        ITerminalBrokerSession brokerSession,
        WebSocket webSocket,
        Action reportActivity,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        await foreach (var brokerEvent in brokerSession.ReadEventsAsync(cancellationToken))
        {
            reportActivity();
            switch (brokerEvent)
            {
                case TerminalOutput output:
                    await webSocket.SendAsync(
                        output.Data,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken);
                    reportBytes(output.Data.Length);
                    break;
                case TerminalExited exited:
                    await SendControlMessageAsync(
                        webSocket,
                        new TerminalServerControlMessage("exited", exited.ExitCode),
                        cancellationToken);
                    return;
                case TerminalBrokerError error:
                    await SendControlMessageAsync(
                        webSocket,
                        new TerminalServerControlMessage(
                            "error",
                            Code: error.Code,
                            Message: error.Message),
                        cancellationToken);
                    return;
            }
        }
    }

    private static Task SendControlMessageAsync(
        WebSocket webSocket,
        TerminalServerControlMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            TerminalApiJsonContext.Default.TerminalServerControlMessage);
        return webSocket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task ObserveRelayTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
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
