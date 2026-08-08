//--------------------------//
//--------鉴权并映射 RAID 生命周期预检与 Operation 协议---------//
//--------Authorizes and maps RAID lifecycle previews and Operation protocols--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.RaidManagement;

namespace Nas.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationDefaults.RaidManagePolicy)]
[Route("api/raid")]
public sealed class RaidManagementController(
    IRaidManagementService raid,
    ILogger<RaidManagementController> logger) : ControllerBase
{
    [HttpPost("operation-previews")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("raid-preview")]
    [ProducesResponseType<RaidOperationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RaidOperationPreviewResponse>> CreatePreview(
        CreateRaidOperationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }
        if (!TryParseAction(request.Action, out var action))
        {
            return InvalidRequest([
                new RaidOperationIssue("raid.action_invalid", "action", "RAID 操作类型不受支持")
            ]);
        }

        var outcome = await raid.CreatePreviewAsync(
            userId.Value,
            request.Password,
            new RequestedRaidOperation(
                action,
                request.ArrayId,
                request.ArrayName,
                request.Level,
                request.DeviceIds ?? [],
                request.SourceDeviceId,
                request.TargetDeviceCount),
            cancellationToken);
        return outcome switch
        {
            RaidPreviewCreated created => Ok(RaidOperationPreviewResponse.From(created.Preview)),
            RaidPreviewRejected { Failure: RaidPreviewFailure.InvalidRequest } rejected =>
                InvalidRequest(rejected.Issues),
            RaidPreviewRejected { Failure: RaidPreviewFailure.ReauthenticationFailed } =>
                ReauthenticationFailed(userId.Value),
            _ => throw new InvalidOperationException("Unknown RAID preview outcome")
        };
    }

    [HttpPost("operations")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("raid-change")]
    [ProducesResponseType<RaidOperationResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<RaidOperationResponse>> Execute(
        ExecuteRaidOperationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }
        var outcome = await raid.ExecuteAsync(
            userId.Value,
            request.Password,
            request.PreviewToken,
            request.ConfirmationPhrase,
            request.IdempotencyKey,
            cancellationToken);
        return outcome switch
        {
            RaidCommandSucceeded succeeded => Accepted(RaidOperationResponse.From(succeeded.Operation)),
            RaidCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException("Unknown RAID execution outcome")
        };
    }

    [HttpGet("operations/{operationId:guid}")]
    [ProducesResponseType<RaidOperationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RaidOperationResponse>> GetOperation(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }
        var outcome = await raid.GetOperationAsync(userId.Value, operationId, cancellationToken);
        return outcome switch
        {
            RaidCommandSucceeded succeeded => Ok(RaidOperationResponse.From(succeeded.Operation)),
            RaidCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException("Unknown RAID operation outcome")
        };
    }

    private Guid? CurrentUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
    }

    private ObjectResult InvalidRequest(IReadOnlyList<RaidOperationIssue> issues) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "RAID 操作参数无效",
        detail: "请修正参数后重新预览",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "RaidRequestInvalid",
            ["issues"] = issues.Select(RaidOperationIssueResponse.From).ToArray()
        });

    private ObjectResult ReauthenticationFailed(Guid userId)
    {
        logger.LogWarning("RAID operation reauthentication failed for user {UserId}", userId);
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "重新认证失败",
            detail: "管理员密码不正确",
            extensions: new Dictionary<string, object?> { ["code"] = "RaidReauthenticationFailed" });
    }

    private ObjectResult CommandFailure(RaidCommandRejected rejected)
    {
        var status = rejected.Failure switch
        {
            RaidCommandFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            RaidCommandFailure.ReauthenticationFailed => StatusCodes.Status401Unauthorized,
            RaidCommandFailure.PreviewExpired or RaidCommandFailure.OperationNotFound =>
                StatusCodes.Status404NotFound,
            RaidCommandFailure.ConfirmationMismatch => StatusCodes.Status400BadRequest,
            RaidCommandFailure.StateChanged or RaidCommandFailure.Conflict =>
                StatusCodes.Status409Conflict,
            RaidCommandFailure.ExecutorUnavailable => StatusCodes.Status503ServiceUnavailable,
            RaidCommandFailure.ExecutorRejected => StatusCodes.Status502BadGateway,
            _ => throw new InvalidOperationException("Unknown RAID command failure")
        };
        return Problem(
            statusCode: status,
            title: "RAID 操作未执行",
            detail: PublicMessage(rejected.Code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = rejected.Code,
                ["retryable"] = rejected.Retryable,
                ["issues"] = rejected.Issues.Select(RaidOperationIssueResponse.From).ToArray()
            });
    }

    private static string PublicMessage(string code) => code switch
    {
        "raid.preview_expired" => "操作预览已过期或已经使用，请重新预览",
        "raid.confirmation_mismatch" => "确认短语不匹配",
        "raid.preview_stale" => "磁盘或阵列状态已经变化，请重新加载并预览",
        "raid.resource_locked" => "目标磁盘或阵列已有操作正在进行",
        "raid.write_unavailable" => "底层 RAID 写入服务尚不可用",
        "privileged.unavailable_before_dispatch" => "底层 RAID 写入服务当前无法连接，操作尚未下发",
        _ => "底层 RAID 操作被拒绝"
    };

    private static bool TryParseAction(string value, out RaidOperationKind action)
    {
        action = value.Trim() switch
        {
            "create" => RaidOperationKind.Create,
            "delete" => RaidOperationKind.Delete,
            "addDevice" => RaidOperationKind.AddDevice,
            "removeDevice" => RaidOperationKind.RemoveDevice,
            "replaceDevice" => RaidOperationKind.ReplaceDevice,
            "grow" => RaidOperationKind.Grow,
            "shrink" => RaidOperationKind.Shrink,
            _ => (RaidOperationKind)(-1)
        };
        return Enum.IsDefined(action);
    }
}
