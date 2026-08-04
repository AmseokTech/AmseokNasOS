//--------------------------//
//--------鉴权并映射网络配置预览与受控变更边界---------//
//--------Authorizes and maps network previews and controlled changes--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.NetworkConfiguration;

namespace Nas.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationDefaults.NetworkManagePolicy)]
[Route("api/network")]
public sealed class NetworkConfigurationController(
    INetworkConfigurationService configurations,
    ILogger<NetworkConfigurationController> logger) : ControllerBase
{
    [HttpPost("configuration-previews")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("network-preview")]
    [ProducesResponseType<NetworkConfigurationPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkConfigurationPreviewResponse>> CreatePreview(
        CreateNetworkConfigurationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!TryParseMode(request.Mode, out var mode))
        {
            return InvalidConfiguration(
            [
                new NetworkConfigurationIssue(
                    "network.mode_invalid",
                    "mode",
                    "网络配置模式必须是 dhcp 或 static")
            ]);
        }

        var outcome = await configurations.CreatePreviewAsync(
            userId.Value,
            request.Password,
            new RequestedNetworkConfiguration(
                request.InterfaceId,
                mode,
                request.IpAddress,
                request.SubnetMask,
                request.Gateway),
            cancellationToken);

        return outcome switch
        {
            NetworkConfigurationPreviewCreated created =>
                Ok(NetworkConfigurationPreviewResponse.From(created.Preview)),
            NetworkConfigurationPreviewRejected
            {
                Failure: NetworkConfigurationPreviewFailure.InvalidConfiguration
            } rejected => InvalidConfiguration(rejected.Issues),
            NetworkConfigurationPreviewRejected
            {
                Failure: NetworkConfigurationPreviewFailure.ReauthenticationFailed
            } => ReauthenticationFailed(userId.Value),
            NetworkConfigurationPreviewRejected
            {
                Failure: NetworkConfigurationPreviewFailure.InterfaceNotFound
            } => InterfaceNotFound(),
            _ => throw new InvalidOperationException(
                "Network configuration service returned an unknown outcome")
        };
    }

    [HttpPost("configuration-operations")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("network-change")]
    [ProducesResponseType<NetworkConfigurationOperationResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NetworkConfigurationOperationResponse>> Apply(
        ApplyNetworkConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!TryParseMode(request.Mode, out var mode))
        {
            return CommandFailure(
                new NetworkConfigurationCommandRejected(
                    NetworkConfigurationCommandFailure.InvalidConfiguration,
                    "network.configuration_invalid",
                    Retryable: false,
                    [
                        new NetworkConfigurationIssue(
                            "network.mode_invalid",
                            "mode",
                            "网络配置模式必须是 dhcp 或 static")
                    ]));
        }

        var outcome = await configurations.ApplyAsync(
            userId.Value,
            request.Password,
            new RequestedNetworkConfiguration(
                request.InterfaceId,
                mode,
                request.IpAddress,
                request.SubnetMask,
                request.Gateway),
            cancellationToken);

        return outcome switch
        {
            NetworkConfigurationCommandSucceeded succeeded =>
                Accepted(NetworkConfigurationOperationResponse.From(succeeded.Operation)),
            NetworkConfigurationCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException(
                "Network configuration service returned an unknown apply outcome")
        };
    }

    [HttpPost("configuration-operations/{operationId:guid}/confirm")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("network-change")]
    [ProducesResponseType<NetworkConfigurationOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NetworkConfigurationOperationResponse>> Confirm(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await CompleteOperationCommand(
            operationId,
            configurations.ConfirmAsync,
            cancellationToken);
    }

    [HttpPost("configuration-operations/{operationId:guid}/rollback")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("network-change")]
    [ProducesResponseType<NetworkConfigurationOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NetworkConfigurationOperationResponse>> Rollback(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await CompleteOperationCommand(
            operationId,
            configurations.RollbackAsync,
            cancellationToken);
    }

    private async Task<ActionResult<NetworkConfigurationOperationResponse>>
        CompleteOperationCommand(
            Guid operationId,
            Func<Guid, Guid, CancellationToken, Task<NetworkConfigurationCommandOutcome>> command,
            CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var outcome = await command(userId.Value, operationId, cancellationToken);
        return outcome switch
        {
            NetworkConfigurationCommandSucceeded succeeded =>
                Ok(NetworkConfigurationOperationResponse.From(succeeded.Operation)),
            NetworkConfigurationCommandRejected rejected => CommandFailure(rejected),
            _ => throw new InvalidOperationException(
                "Network configuration service returned an unknown command outcome")
        };
    }

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private ObjectResult InvalidConfiguration(IReadOnlyList<NetworkConfigurationIssue> issues)
    {
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "网络配置无效",
            detail: "请修正网络参数后重新预览",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "NetworkConfigurationInvalid",
                ["issues"] = issues.Select(NetworkConfigurationIssueResponse.From).ToArray()
            });
    }

    private ObjectResult ReauthenticationFailed(Guid userId)
    {
        logger.LogWarning(
            "Network configuration preview reauthentication failed for user {UserId}",
            userId);
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "重新认证失败",
            detail: "管理员密码不正确",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "NetworkReauthenticationFailed"
            });
    }

    private ObjectResult InterfaceNotFound()
    {
        return Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "目标网卡不存在",
            detail: "目标物理网卡已不存在或身份已经变化，请重新加载网卡列表",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "NetworkInterfaceNotFound"
            });
    }

    private ObjectResult CommandFailure(NetworkConfigurationCommandRejected rejected)
    {
        var status = rejected.Failure switch
        {
            NetworkConfigurationCommandFailure.InvalidConfiguration =>
                StatusCodes.Status400BadRequest,
            NetworkConfigurationCommandFailure.ReauthenticationFailed =>
                StatusCodes.Status401Unauthorized,
            NetworkConfigurationCommandFailure.InterfaceNotFound or
            NetworkConfigurationCommandFailure.OperationNotFound =>
                StatusCodes.Status404NotFound,
            NetworkConfigurationCommandFailure.Conflict => StatusCodes.Status409Conflict,
            NetworkConfigurationCommandFailure.ExecutorUnavailable =>
                StatusCodes.Status503ServiceUnavailable,
            NetworkConfigurationCommandFailure.ExecutorRejected =>
                StatusCodes.Status502BadGateway,
            _ => throw new InvalidOperationException("Unknown network command failure")
        };
        var title = rejected.Failure switch
        {
            NetworkConfigurationCommandFailure.InvalidConfiguration => "网络配置无效",
            NetworkConfigurationCommandFailure.ReauthenticationFailed => "重新认证失败",
            NetworkConfigurationCommandFailure.InterfaceNotFound => "目标网卡不存在",
            NetworkConfigurationCommandFailure.OperationNotFound => "网络变更操作不存在",
            NetworkConfigurationCommandFailure.Conflict => "网络变更状态冲突",
            NetworkConfigurationCommandFailure.ExecutorUnavailable => "网络变更服务不可用",
            NetworkConfigurationCommandFailure.ExecutorRejected => "网络变更被拒绝",
            _ => throw new InvalidOperationException("Unknown network command failure")
        };

        return Problem(
            statusCode: status,
            title: title,
            detail: "网络配置未被修改",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = rejected.Code,
                ["retryable"] = rejected.Retryable,
                ["issues"] = rejected.Issues
                    .Select(NetworkConfigurationIssueResponse.From)
                    .ToArray()
            });
    }

    private static bool TryParseMode(string value, out NetworkAddressingMode mode)
    {
        if (string.Equals(value, "dhcp", StringComparison.OrdinalIgnoreCase))
        {
            mode = NetworkAddressingMode.Dhcp;
            return true;
        }
        if (string.Equals(value, "static", StringComparison.OrdinalIgnoreCase))
        {
            mode = NetworkAddressingMode.StaticIpv4;
            return true;
        }

        mode = default;
        return false;
    }
}
