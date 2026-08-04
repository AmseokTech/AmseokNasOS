//--------------------------//
//--------在 Rust 写入边界启用前拒绝所有网络配置命令---------//
//--------Rejects network configuration commands until the Rust write boundary is enabled--------//
//-------------------------//
using Nas.Application.NetworkConfiguration;

namespace Nas.Infrastructure.Privileged;

public sealed class UnavailableNetworkConfigurationExecutor : INetworkConfigurationExecutor
{
    private static readonly NetworkConfigurationExecutionOutcome Unavailable =
        new NetworkConfigurationExecutionRejected(
            NetworkConfigurationExecutionFailure.Unavailable,
            "network.write_unavailable",
            Retryable: false);

    public Task<NetworkConfigurationExecutionOutcome> ApplyAsync(
        Guid operationId,
        Guid userId,
        string interfaceId,
        NormalizedNetworkConfiguration configuration,
        DateTimeOffset confirmationDeadline,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Unavailable);
    }

    public Task<NetworkConfigurationExecutionOutcome> ConfirmAsync(
        Guid operationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Unavailable);
    }

    public Task<NetworkConfigurationExecutionOutcome> RollbackAsync(
        Guid operationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Unavailable);
    }
}
