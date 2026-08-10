//--------------------------//
//--------鉴权并映射数据卷供应、权限、校验与共享管理协议---------//
//--------Authorizes and maps volume, permission, verification, and share operations--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.StorageManagement;

namespace Nas.Api.Controllers;

[ApiController]
[Route("api/storage-management")]
public sealed class StorageManagementController(
    IStorageManagementService storage,
    ILogger<StorageManagementController> logger) : ControllerBase
{
    [HttpGet("volumes")]
    [Authorize(Policy = AuthenticationDefaults.StorageReadPolicy)]
    public async Task<ActionResult<IReadOnlyList<ManagedVolumeResponse>>> GetVolumes(
        CancellationToken cancellationToken)
    {
        var volumes = await storage.GetVolumesAsync(cancellationToken);
        return Ok(volumes.Select(ManagedVolumeResponse.From).ToArray());
    }

    [HttpPost("operation-previews")]
    [Authorize(Policy = AuthenticationDefaults.StorageManagePolicy)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("storage-preview")]
    public async Task<ActionResult<StorageOperationPreviewResponse>> CreatePreview(
        CreateStorageOperationPreviewRequest request,
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
                new StorageOperationIssue("storage.action_invalid", "action", "存储操作类型不受支持")
            ]);
        }
        var outcome = await storage.CreatePreviewAsync(
            userId.Value,
            request.Password,
            new RequestedStorageOperation(
                action,
                request.ArrayId,
                request.VolumeId,
                request.VolumeName,
                request.OwnerName,
                request.GroupName,
                request.DirectoryMode,
                request.Smb is null ? null : new SmbShareSettings(
                    request.Smb.Enabled,
                    request.Smb.ShareName,
                    request.Smb.ReadOnly,
                    request.Smb.GuestAccess,
                    request.Smb.AllowedNetwork),
                request.Nfs is null ? null : new NfsShareSettings(
                    request.Nfs.Enabled,
                    request.Nfs.ClientNetwork,
                    request.Nfs.ReadOnly)),
            cancellationToken);
        return outcome switch
        {
            StoragePreviewCreated created => Ok(StorageOperationPreviewResponse.From(created.Preview)),
            StoragePreviewRejected { Failure: StoragePreviewFailure.InvalidRequest } rejected =>
                InvalidRequest(rejected.Issues),
            StoragePreviewRejected { Failure: StoragePreviewFailure.ReauthenticationFailed } =>
                ReauthenticationFailed(userId.Value),
            _ => throw new InvalidOperationException("Unknown storage preview outcome")
        };
    }

    [HttpPost("operations")]
    [Authorize(Policy = AuthenticationDefaults.StorageManagePolicy)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("storage-change")]
    public async Task<ActionResult<StorageOperationResponse>> Execute(
        ExecuteStorageOperationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }
        var outcome = await storage.ExecuteAsync(
            userId.Value,
            request.Password,
            request.PreviewToken,
            request.ConfirmationPhrase,
            request.IdempotencyKey,
            cancellationToken);
        return outcome switch
        {
            StorageCommandSucceeded succeeded => Accepted(StorageOperationResponse.From(succeeded.Operation)),
            StorageCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException("Unknown storage execution outcome")
        };
    }

    [HttpGet("operations/{operationId:guid}")]
    [Authorize(Policy = AuthenticationDefaults.StorageManagePolicy)]
    public async Task<ActionResult<StorageOperationResponse>> GetOperation(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }
        var outcome = await storage.GetOperationAsync(userId.Value, operationId, cancellationToken);
        return outcome switch
        {
            StorageCommandSucceeded succeeded => Ok(StorageOperationResponse.From(succeeded.Operation)),
            StorageCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException("Unknown storage operation outcome")
        };
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId : null;

    private ObjectResult InvalidRequest(IReadOnlyList<StorageOperationIssue> issues) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "存储操作参数无效",
        detail: "请修正参数后重新预览",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "StorageRequestInvalid",
            ["issues"] = issues.Select(StorageOperationIssueResponse.From).ToArray()
        });

    private ObjectResult ReauthenticationFailed(Guid userId)
    {
        logger.LogWarning("Storage operation reauthentication failed for user {UserId}", userId);
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "重新认证失败",
            detail: "管理员密码不正确",
            extensions: new Dictionary<string, object?> { ["code"] = "StorageReauthenticationFailed" });
    }

    private ObjectResult CommandFailure(StorageCommandRejected rejected)
    {
        var status = rejected.Failure switch
        {
            StorageCommandFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            StorageCommandFailure.ReauthenticationFailed => StatusCodes.Status401Unauthorized,
            StorageCommandFailure.PreviewExpired or StorageCommandFailure.OperationNotFound =>
                StatusCodes.Status404NotFound,
            StorageCommandFailure.ConfirmationMismatch => StatusCodes.Status400BadRequest,
            StorageCommandFailure.StateChanged or StorageCommandFailure.Conflict =>
                StatusCodes.Status409Conflict,
            StorageCommandFailure.ExecutorUnavailable => StatusCodes.Status503ServiceUnavailable,
            StorageCommandFailure.ExecutorRejected => StatusCodes.Status502BadGateway,
            _ => throw new InvalidOperationException("Unknown storage command failure")
        };
        return Problem(
            statusCode: status,
            title: "存储操作未执行",
            detail: PublicMessage(rejected.Code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = rejected.Code,
                ["retryable"] = rejected.Retryable,
                ["issues"] = rejected.Issues.Select(StorageOperationIssueResponse.From).ToArray()
            });
    }

    private static string PublicMessage(string code) => code switch
    {
        "storage.preview_expired" => "操作预览已过期或已经使用，请重新预览",
        "storage.confirmation_mismatch" => "确认短语不匹配",
        "storage.preview_stale" => "阵列、数据卷或共享状态已经变化，请重新加载",
        "storage.resource_locked" => "目标阵列或数据卷已有操作正在进行",
        "storage.write_unavailable" => "底层数据卷与共享服务尚不可用",
        "privileged.unavailable_before_dispatch" => "底层服务当前无法连接，操作尚未下发",
        _ => "底层存储操作被拒绝"
    };

    private static bool TryParseAction(string value, out StorageOperationKind action)
    {
        action = value.Trim() switch
        {
            "provisionVolume" => StorageOperationKind.ProvisionVolume,
            "updatePermissions" => StorageOperationKind.UpdatePermissions,
            "configureShares" => StorageOperationKind.ConfigureShares,
            "verifyReadWrite" => StorageOperationKind.VerifyReadWrite,
            _ => (StorageOperationKind)(-1)
        };
        return Enum.IsDefined(action);
    }
}
