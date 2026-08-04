//--------------------------//
//--------编排网络配置重新认证、规范化与受控变更---------//
//--------Orchestrates network reauthentication, normalization, and controlled changes--------//
//-------------------------//
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Numerics;
using Nas.Application.Authentication;

namespace Nas.Application.NetworkConfiguration;

public sealed class NetworkConfigurationService(
    IAuthenticationService authentication,
    INetworkConfigurationInventory inventory,
    INetworkConfigurationExecutor executor,
    TimeProvider timeProvider) : INetworkConfigurationService
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(2);
    private static readonly IReadOnlyList<string> WriteUnavailable =
        ["network.write_unavailable"];

    private static readonly IReadOnlyList<string> PreviewWarnings =
    [
        "network.management_connection_may_be_interrupted",
        "network.connectivity_confirmation_and_automatic_rollback_required"
    ];

    public async Task<NetworkConfigurationPreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedNetworkConfiguration requested,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(requested);
        if (normalized.Issues.Count > 0)
        {
            return new NetworkConfigurationPreviewRejected(
                NetworkConfigurationPreviewFailure.InvalidConfiguration,
                normalized.Issues);
        }

        if (!await authentication.VerifyPasswordAsync(
                userId,
                password,
                cancellationToken))
        {
            return new NetworkConfigurationPreviewRejected(
                NetworkConfigurationPreviewFailure.ReauthenticationFailed,
                []);
        }

        var interfaces = await inventory.InspectInterfacesAsync(cancellationToken);
        var targets = interfaces
            .Where(item => string.Equals(
                item.Id,
                normalized.InterfaceId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (targets.Length != 1 || !IsComplete(targets[0]))
        {
            return new NetworkConfigurationPreviewRejected(
                NetworkConfigurationPreviewFailure.InterfaceNotFound,
                []);
        }
        var target = targets[0];

        return new NetworkConfigurationPreviewCreated(
            new NetworkConfigurationPreview(
                target.Id,
                target.Name,
                target.ConfigurationMode,
                target.Addresses,
                target.Gateway,
                normalized.Configuration!,
                CanApply: false,
                WriteUnavailable,
                PreviewWarnings));
    }

    public async Task<NetworkConfigurationCommandOutcome> ApplyAsync(
        Guid userId,
        string password,
        RequestedNetworkConfiguration requested,
        CancellationToken cancellationToken)
    {
        var previewOutcome = await CreatePreviewAsync(
            userId,
            password,
            requested,
            cancellationToken);
        if (previewOutcome is NetworkConfigurationPreviewRejected rejected)
        {
            return FromPreviewRejection(rejected);
        }

        var preview = ((NetworkConfigurationPreviewCreated)previewOutcome).Preview;
        var operationId = Guid.NewGuid();
        var confirmationDeadline = timeProvider.GetUtcNow().Add(ConfirmationWindow);
        var execution = await executor.ApplyAsync(
            operationId,
            userId,
            preview.InterfaceId,
            preview.Requested,
            confirmationDeadline,
            cancellationToken);

        return FromExecution(
            execution,
            operationId,
            NetworkConfigurationOperationState.AwaitingConfirmation);
    }

    public async Task<NetworkConfigurationCommandOutcome> ConfirmAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            return OperationNotFound();
        }

        var execution = await executor.ConfirmAsync(
            operationId,
            userId,
            cancellationToken);
        return FromExecution(
            execution,
            operationId,
            NetworkConfigurationOperationState.Confirmed);
    }

    public async Task<NetworkConfigurationCommandOutcome> RollbackAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            return OperationNotFound();
        }

        var execution = await executor.RollbackAsync(
            operationId,
            userId,
            cancellationToken);
        return FromExecution(
            execution,
            operationId,
            NetworkConfigurationOperationState.RolledBack);
    }

    private static NetworkConfigurationCommandOutcome FromPreviewRejection(
        NetworkConfigurationPreviewRejected rejected)
    {
        var failure = rejected.Failure switch
        {
            NetworkConfigurationPreviewFailure.InvalidConfiguration =>
                NetworkConfigurationCommandFailure.InvalidConfiguration,
            NetworkConfigurationPreviewFailure.ReauthenticationFailed =>
                NetworkConfigurationCommandFailure.ReauthenticationFailed,
            NetworkConfigurationPreviewFailure.InterfaceNotFound =>
                NetworkConfigurationCommandFailure.InterfaceNotFound,
            _ => throw new InvalidOperationException("Unknown preview rejection")
        };
        var code = rejected.Failure switch
        {
            NetworkConfigurationPreviewFailure.InvalidConfiguration =>
                "network.configuration_invalid",
            NetworkConfigurationPreviewFailure.ReauthenticationFailed =>
                "network.reauthentication_failed",
            NetworkConfigurationPreviewFailure.InterfaceNotFound =>
                "network.interface_not_found",
            _ => throw new InvalidOperationException("Unknown preview rejection")
        };

        return new NetworkConfigurationCommandRejected(
            failure,
            code,
            Retryable: false,
            rejected.Issues);
    }

    private static NetworkConfigurationCommandOutcome FromExecution(
        NetworkConfigurationExecutionOutcome execution,
        Guid expectedOperationId,
        NetworkConfigurationOperationState expectedState)
    {
        if (execution is NetworkConfigurationExecutionSucceeded succeeded)
        {
            // The executor cannot substitute an identity or skip the control-plane state transition.
            var deadlineIsValid = expectedState ==
                    NetworkConfigurationOperationState.AwaitingConfirmation
                ? succeeded.Operation.ConfirmationDeadline is not null
                : succeeded.Operation.ConfirmationDeadline is null;
            if (succeeded.Operation.Id != expectedOperationId
                || succeeded.Operation.State != expectedState
                || !deadlineIsValid)
            {
                return new NetworkConfigurationCommandRejected(
                    NetworkConfigurationCommandFailure.ExecutorRejected,
                    "network.operation_result_mismatch",
                    Retryable: false,
                    []);
            }

            return new NetworkConfigurationCommandSucceeded(succeeded.Operation);
        }

        var rejected = (NetworkConfigurationExecutionRejected)execution;
        var failure = rejected.Failure switch
        {
            NetworkConfigurationExecutionFailure.Unavailable =>
                NetworkConfigurationCommandFailure.ExecutorUnavailable,
            NetworkConfigurationExecutionFailure.OperationNotFound =>
                NetworkConfigurationCommandFailure.OperationNotFound,
            NetworkConfigurationExecutionFailure.Conflict =>
                NetworkConfigurationCommandFailure.Conflict,
            NetworkConfigurationExecutionFailure.Rejected =>
                NetworkConfigurationCommandFailure.ExecutorRejected,
            _ => throw new InvalidOperationException("Unknown executor rejection")
        };
        return new NetworkConfigurationCommandRejected(
            failure,
            rejected.Code,
            rejected.Retryable,
            []);
    }

    private static NetworkConfigurationCommandRejected OperationNotFound()
    {
        return new(
            NetworkConfigurationCommandFailure.OperationNotFound,
            "network.operation_not_found",
            Retryable: false,
            []);
    }

    private static NormalizationResult Normalize(RequestedNetworkConfiguration requested)
    {
        var issues = new List<NetworkConfigurationIssue>();
        var interfaceId = requested.InterfaceId.Trim();
        if (interfaceId.Length == 0)
        {
            issues.Add(Issue(
                "network.interface_id_required",
                "interfaceId",
                "必须选择目标物理网卡"));
        }
        else if (interfaceId.Length > 128)
        {
            issues.Add(Issue(
                "network.interface_id_too_long",
                "interfaceId",
                "目标网卡标识过长"));
        }

        if (requested.Mode is not NetworkAddressingMode.Dhcp
            and not NetworkAddressingMode.StaticIpv4)
        {
            issues.Add(Issue(
                "network.mode_invalid",
                "mode",
                "网络配置模式不受支持"));
            return new NormalizationResult(interfaceId, null, issues);
        }

        if (requested.Mode == NetworkAddressingMode.Dhcp)
        {
            if (HasValue(requested.IpAddress)
                || HasValue(requested.SubnetMask)
                || HasValue(requested.Gateway))
            {
                issues.Add(Issue(
                    "network.dhcp_static_fields_forbidden",
                    "mode",
                    "DHCP 模式不能同时指定 IP 地址、子网掩码或网关"));
            }

            return new NormalizationResult(
                interfaceId,
                issues.Count == 0
                    ? new NormalizedNetworkConfiguration(
                        NetworkAddressingMode.Dhcp,
                        null,
                        null,
                        null,
                        null)
                    : null,
                issues);
        }

        var address = ParseRequiredIpv4(
            requested.IpAddress,
            "ipAddress",
            "network.ip_address_required",
            "network.ip_address_invalid",
            issues);
        var mask = ParseSubnetMask(requested.SubnetMask, issues);
        var gateway = ParseOptionalIpv4(
            requested.Gateway,
            "gateway",
            "network.gateway_invalid",
            issues);

        if (address is not null && mask is not null)
        {
            ValidateHostAddress(address, mask, issues);
        }
        if (gateway is not null && mask is not null)
        {
            ValidateGateway(address, gateway, mask, issues);
        }

        return new NormalizationResult(
            interfaceId,
            issues.Count == 0
                ? new NormalizedNetworkConfiguration(
                    NetworkAddressingMode.StaticIpv4,
                    address!.ToString(),
                    mask!.Address.ToString(),
                    mask.PrefixLength,
                    gateway?.ToString())
                : null,
            issues);
    }

    private static IPAddress? ParseRequiredIpv4(
        string? value,
        string field,
        string requiredCode,
        string invalidCode,
        ICollection<NetworkConfigurationIssue> issues)
    {
        if (!HasValue(value))
        {
            issues.Add(Issue(requiredCode, field, "固定地址模式必须填写 IPv4 地址"));
            return null;
        }

        return ParseIpv4(value!, field, invalidCode, issues);
    }

    private static IPAddress? ParseOptionalIpv4(
        string? value,
        string field,
        string invalidCode,
        ICollection<NetworkConfigurationIssue> issues)
    {
        return HasValue(value) ? ParseIpv4(value!, field, invalidCode, issues) : null;
    }

    private static IPAddress? ParseIpv4(
        string value,
        string field,
        string invalidCode,
        ICollection<NetworkConfigurationIssue> issues)
    {
        if (!TryParseStrictIpv4(value, out var address))
        {
            issues.Add(Issue(invalidCode, field, "必须使用有效的 IPv4 地址"));
            return null;
        }

        return address;
    }

    private static ParsedSubnetMask? ParseSubnetMask(
        string? value,
        ICollection<NetworkConfigurationIssue> issues)
    {
        if (!HasValue(value))
        {
            issues.Add(Issue(
                "network.subnet_mask_required",
                "subnetMask",
                "固定地址模式必须填写子网掩码"));
            return null;
        }

        if (!TryParseStrictIpv4(value!, out var address))
        {
            issues.Add(Issue(
                "network.subnet_mask_invalid",
                "subnetMask",
                "必须使用有效的连续 IPv4 子网掩码"));
            return null;
        }

        var numeric = ToUInt32(address);
        var inverted = ~numeric;
        var prefixLength = BitOperations.PopCount(numeric);
        if ((inverted & (inverted + 1)) != 0 || prefixLength is < 1 or > 30)
        {
            issues.Add(Issue(
                "network.subnet_mask_invalid",
                "subnetMask",
                "子网掩码必须连续且前缀长度在 1 到 30 之间"));
            return null;
        }

        return new ParsedSubnetMask(address, numeric, prefixLength);
    }

    private static void ValidateHostAddress(
        IPAddress address,
        ParsedSubnetMask mask,
        ICollection<NetworkConfigurationIssue> issues)
    {
        var numeric = ToUInt32(address);
        if (!IsUnicast(address)
            || numeric == (numeric & mask.Numeric)
            || numeric == ((numeric & mask.Numeric) | ~mask.Numeric))
        {
            issues.Add(Issue(
                "network.ip_address_unusable",
                "ipAddress",
                "IP 地址不能是未指定、回环、组播、网络或广播地址"));
        }
    }

    private static void ValidateGateway(
        IPAddress? address,
        IPAddress gateway,
        ParsedSubnetMask mask,
        ICollection<NetworkConfigurationIssue> issues)
    {
        var gatewayNumeric = ToUInt32(gateway);
        if (!IsUnicast(gateway)
            || gatewayNumeric == (gatewayNumeric & mask.Numeric)
            || gatewayNumeric == ((gatewayNumeric & mask.Numeric) | ~mask.Numeric))
        {
            issues.Add(Issue(
                "network.gateway_unusable",
                "gateway",
                "网关不能是未指定、回环、组播、网络或广播地址"));
            return;
        }

        if (address is null)
        {
            return;
        }

        var addressNumeric = ToUInt32(address);
        if (addressNumeric == gatewayNumeric)
        {
            issues.Add(Issue(
                "network.gateway_matches_address",
                "gateway",
                "网关不能与 IP 地址相同"));
        }
        else if ((addressNumeric & mask.Numeric) != (gatewayNumeric & mask.Numeric))
        {
            issues.Add(Issue(
                "network.gateway_outside_subnet",
                "gateway",
                "网关必须与 IP 地址位于同一子网"));
        }
    }

    private static bool IsUnicast(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return !address.Equals(IPAddress.Any)
            && !IPAddress.IsLoopback(address)
            && bytes[0] < 224;
    }

    private static bool IsComplete(NetworkConfigurationInterfaceSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.Id)
            && !string.IsNullOrWhiteSpace(snapshot.Name)
            && !string.IsNullOrWhiteSpace(snapshot.ConfigurationMode)
            && snapshot.Addresses is not null;
    }

    private static bool TryParseStrictIpv4(string value, out IPAddress address)
    {
        var parts = value.Trim().Split('.');
        var bytes = new byte[4];
        if (parts.Length != bytes.Length)
        {
            address = IPAddress.None;
            return false;
        }

        for (var index = 0; index < bytes.Length; index++)
        {
            if (parts[index].Length == 0
                || !byte.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out bytes[index]))
            {
                address = IPAddress.None;
                return false;
            }
        }

        address = new IPAddress(bytes);
        return true;
    }

    private static uint ToUInt32(IPAddress address)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static NetworkConfigurationIssue Issue(
        string code,
        string field,
        string message) => new(code, field, message);

    private sealed record ParsedSubnetMask(
        IPAddress Address,
        uint Numeric,
        int PrefixLength);

    private sealed record NormalizationResult(
        string InterfaceId,
        NormalizedNetworkConfiguration? Configuration,
        IReadOnlyList<NetworkConfigurationIssue> Issues);
}
