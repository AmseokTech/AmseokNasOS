//--------------------------//
//--------定义网络配置预览、变更用例与外部执行端口---------//
//--------Defines network previews, change use cases, and external execution ports--------//
//-------------------------//
namespace Nas.Application.NetworkConfiguration;

public enum NetworkAddressingMode
{
    Dhcp,
    StaticIpv4
}

public sealed record RequestedNetworkConfiguration(
    string InterfaceId,
    NetworkAddressingMode Mode,
    string? IpAddress,
    string? SubnetMask,
    string? Gateway);

public sealed record NormalizedNetworkConfiguration(
    NetworkAddressingMode Mode,
    string? IpAddress,
    string? SubnetMask,
    int? PrefixLength,
    string? Gateway);

public sealed record NetworkConfigurationInterfaceSnapshot(
    string Id,
    string Name,
    string ConfigurationMode,
    IReadOnlyList<string> Addresses,
    string? Gateway);

public sealed record NetworkConfigurationPreview(
    string InterfaceId,
    string InterfaceName,
    string CurrentMode,
    IReadOnlyList<string> CurrentAddresses,
    string? CurrentGateway,
    NormalizedNetworkConfiguration Requested,
    bool CanApply,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings);

public sealed record NetworkConfigurationIssue(
    string Code,
    string Field,
    string Message);

public enum NetworkConfigurationPreviewFailure
{
    InvalidConfiguration,
    ReauthenticationFailed,
    InterfaceNotFound
}

public abstract record NetworkConfigurationPreviewOutcome;

public sealed record NetworkConfigurationPreviewCreated(NetworkConfigurationPreview Preview)
    : NetworkConfigurationPreviewOutcome;

public sealed record NetworkConfigurationPreviewRejected(
    NetworkConfigurationPreviewFailure Failure,
    IReadOnlyList<NetworkConfigurationIssue> Issues)
    : NetworkConfigurationPreviewOutcome;

public enum NetworkConfigurationOperationState
{
    AwaitingConfirmation,
    Confirmed,
    RolledBack
}

public sealed record NetworkConfigurationOperation(
    Guid Id,
    NetworkConfigurationOperationState State,
    DateTimeOffset? ConfirmationDeadline);

public enum NetworkConfigurationCommandFailure
{
    InvalidConfiguration,
    ReauthenticationFailed,
    InterfaceNotFound,
    OperationNotFound,
    Conflict,
    ExecutorUnavailable,
    ExecutorRejected
}

public abstract record NetworkConfigurationCommandOutcome;

public sealed record NetworkConfigurationCommandSucceeded(
    NetworkConfigurationOperation Operation)
    : NetworkConfigurationCommandOutcome;

public sealed record NetworkConfigurationCommandRejected(
    NetworkConfigurationCommandFailure Failure,
    string Code,
    bool Retryable,
    IReadOnlyList<NetworkConfigurationIssue> Issues)
    : NetworkConfigurationCommandOutcome;

public enum NetworkConfigurationExecutionFailure
{
    Unavailable,
    OperationNotFound,
    Conflict,
    Rejected
}

public abstract record NetworkConfigurationExecutionOutcome;

public sealed record NetworkConfigurationExecutionSucceeded(
    NetworkConfigurationOperation Operation)
    : NetworkConfigurationExecutionOutcome;

public sealed record NetworkConfigurationExecutionRejected(
    NetworkConfigurationExecutionFailure Failure,
    string Code,
    bool Retryable)
    : NetworkConfigurationExecutionOutcome;

public interface INetworkConfigurationService
{
    Task<NetworkConfigurationPreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedNetworkConfiguration requested,
        CancellationToken cancellationToken);

    Task<NetworkConfigurationCommandOutcome> ApplyAsync(
        Guid userId,
        string password,
        RequestedNetworkConfiguration requested,
        CancellationToken cancellationToken);

    Task<NetworkConfigurationCommandOutcome> ConfirmAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<NetworkConfigurationCommandOutcome> RollbackAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken);
}

public interface INetworkConfigurationInventory
{
    Task<IReadOnlyList<NetworkConfigurationInterfaceSnapshot>> InspectInterfacesAsync(
        CancellationToken cancellationToken);
}

public interface INetworkConfigurationExecutor
{
    Task<NetworkConfigurationExecutionOutcome> ApplyAsync(
        Guid operationId,
        Guid userId,
        string interfaceId,
        NormalizedNetworkConfiguration configuration,
        DateTimeOffset confirmationDeadline,
        CancellationToken cancellationToken);

    Task<NetworkConfigurationExecutionOutcome> ConfirmAsync(
        Guid operationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<NetworkConfigurationExecutionOutcome> RollbackAsync(
        Guid operationId,
        Guid userId,
        CancellationToken cancellationToken);
}
