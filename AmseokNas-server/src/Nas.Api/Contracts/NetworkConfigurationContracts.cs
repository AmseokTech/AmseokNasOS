//--------------------------//
//--------定义网络配置预览与受控变更的 HTTP 契约---------//
//--------Defines HTTP contracts for network previews and controlled changes--------//
//-------------------------//
using System.ComponentModel.DataAnnotations;
using Nas.Application.NetworkConfiguration;

namespace Nas.Api.Contracts;

public sealed record CreateNetworkConfigurationPreviewRequest(
    [param: Required, MaxLength(128)] string InterfaceId,
    [param: Required, MaxLength(16)] string Mode,
    [param: MaxLength(64)] string? IpAddress,
    [param: MaxLength(64)] string? SubnetMask,
    [param: MaxLength(64)] string? Gateway,
    [param: Required, MaxLength(256)] string Password);

public sealed record ApplyNetworkConfigurationRequest(
    [param: Required, MaxLength(128)] string InterfaceId,
    [param: Required, MaxLength(16)] string Mode,
    [param: MaxLength(64)] string? IpAddress,
    [param: MaxLength(64)] string? SubnetMask,
    [param: MaxLength(64)] string? Gateway,
    [param: Required, MaxLength(256)] string Password);

public sealed record NetworkConfigurationPreviewResponse(
    string InterfaceId,
    string InterfaceName,
    string CurrentMode,
    IReadOnlyList<string> CurrentAddresses,
    string? CurrentGateway,
    string RequestedMode,
    string? RequestedIpAddress,
    string? RequestedSubnetMask,
    int? RequestedPrefixLength,
    string? RequestedGateway,
    bool CanApply,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings)
{
    public static NetworkConfigurationPreviewResponse From(
        NetworkConfigurationPreview preview)
    {
        return new(
            preview.InterfaceId,
            preview.InterfaceName,
            preview.CurrentMode,
            preview.CurrentAddresses,
            preview.CurrentGateway,
            preview.Requested.Mode == NetworkAddressingMode.Dhcp ? "dhcp" : "static",
            preview.Requested.IpAddress,
            preview.Requested.SubnetMask,
            preview.Requested.PrefixLength,
            preview.Requested.Gateway,
            preview.CanApply,
            preview.BlockingReasons,
            preview.Warnings);
    }
}

public sealed record NetworkConfigurationIssueResponse(
    string Code,
    string Field,
    string Message)
{
    public static NetworkConfigurationIssueResponse From(NetworkConfigurationIssue issue)
    {
        return new(issue.Code, issue.Field, issue.Message);
    }
}

public sealed record NetworkConfigurationOperationResponse(
    Guid OperationId,
    string State,
    DateTimeOffset? ConfirmationDeadline)
{
    public static NetworkConfigurationOperationResponse From(
        NetworkConfigurationOperation operation)
    {
        var state = operation.State switch
        {
            NetworkConfigurationOperationState.AwaitingConfirmation =>
                "awaitingConfirmation",
            NetworkConfigurationOperationState.Confirmed => "confirmed",
            NetworkConfigurationOperationState.RolledBack => "rolledBack",
            _ => throw new InvalidOperationException("Unknown network operation state")
        };
        return new(operation.Id, state, operation.ConfirmationDeadline);
    }
}
