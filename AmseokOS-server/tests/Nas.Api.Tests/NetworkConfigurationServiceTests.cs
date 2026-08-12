//--------------------------//
//--------验证网络配置重新认证、校验与安全预览---------//
//--------Verifies network configuration reauthentication, validation, and safe previews--------//
//-------------------------//
using Nas.Application.Authentication;
using Nas.Application.NetworkConfiguration;

namespace Nas.Api.Tests;

public sealed class NetworkConfigurationServiceTests
{
    [Fact]
    public async Task DhcpPreviewReauthenticatesAndAllowsTheGuardedWriteFlow()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(authentication, inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                inventory.Interface.Id,
                NetworkAddressingMode.Dhcp,
                null,
                null,
                null),
            CancellationToken.None);

        var created = Assert.IsType<NetworkConfigurationPreviewCreated>(outcome);
        Assert.Equal(NetworkAddressingMode.Dhcp, created.Preview.Requested.Mode);
        Assert.Null(created.Preview.Requested.IpAddress);
        Assert.True(created.Preview.CanApply);
        Assert.Empty(created.Preview.BlockingReasons);
        Assert.Equal(1, authentication.VerificationCount);
        Assert.Equal(1, inventory.InspectionCount);
    }

    [Fact]
    public async Task StaticPreviewNormalizesAddressMaskPrefixAndGateway()
    {
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                $"  {inventory.Interface.Id.ToUpperInvariant()}  ",
                NetworkAddressingMode.StaticIpv4,
                " 192.168.10.20 ",
                " 255.255.255.0 ",
                " 192.168.10.1 "),
            CancellationToken.None);

        var preview = Assert.IsType<NetworkConfigurationPreviewCreated>(outcome).Preview;
        Assert.Equal(inventory.Interface.Id, preview.InterfaceId);
        Assert.Equal(NetworkAddressingMode.StaticIpv4, preview.Requested.Mode);
        Assert.Equal("192.168.10.20", preview.Requested.IpAddress);
        Assert.Equal("255.255.255.0", preview.Requested.SubnetMask);
        Assert.Equal(24, preview.Requested.PrefixLength);
        Assert.Equal("192.168.10.1", preview.Requested.Gateway);
    }

    [Fact]
    public async Task DhcpRejectsStaticFieldsBeforeReauthentication()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(authentication, inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                inventory.Interface.Id,
                NetworkAddressingMode.Dhcp,
                "192.168.1.20",
                null,
                null),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Equal(NetworkConfigurationPreviewFailure.InvalidConfiguration, rejected.Failure);
        Assert.Contains(
            rejected.Issues,
            issue => issue.Code == "network.dhcp_static_fields_forbidden");
        Assert.Equal(0, authentication.VerificationCount);
        Assert.Equal(0, inventory.InspectionCount);
    }

    [Fact]
    public async Task UnknownApplicationModeFailsClosedBeforeReauthentication()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(authentication, inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                inventory.Interface.Id,
                (NetworkAddressingMode)99,
                null,
                null,
                null),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Contains(rejected.Issues, issue => issue.Code == "network.mode_invalid");
        Assert.Equal(0, authentication.VerificationCount);
        Assert.Equal(0, inventory.InspectionCount);
    }

    [Theory]
    [InlineData("255.0.255.0")]
    [InlineData("255.255.255.254")]
    [InlineData("0.0.0.0")]
    public async Task StaticRejectsUnsupportedSubnetMasks(string subnetMask)
    {
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            new NetworkConfigurationInventoryStub());

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                "mac:00:11:22:33:44:55",
                NetworkAddressingMode.StaticIpv4,
                "192.168.1.20",
                subnetMask,
                "192.168.1.1"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Contains(
            rejected.Issues,
            issue => issue.Code == "network.subnet_mask_invalid");
    }

    [Fact]
    public async Task StaticRejectsGatewayOutsideTheSelectedSubnet()
    {
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            new NetworkConfigurationInventoryStub());

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                "mac:00:11:22:33:44:55",
                NetworkAddressingMode.StaticIpv4,
                "192.168.1.20",
                "255.255.255.0",
                "192.168.2.1"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Contains(
            rejected.Issues,
            issue => issue.Code == "network.gateway_outside_subnet");
    }

    [Theory]
    [InlineData("192.168.1")]
    [InlineData("0xC0.168.1.20")]
    [InlineData("192.168.1.256")]
    public async Task StaticRejectsAmbiguousOrOutOfRangeIpv4Syntax(string ipAddress)
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(authentication, inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                inventory.Interface.Id,
                NetworkAddressingMode.StaticIpv4,
                ipAddress,
                "255.255.255.0",
                "192.168.1.1"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Contains(
            rejected.Issues,
            issue => issue.Code == "network.ip_address_invalid");
        Assert.Equal(0, authentication.VerificationCount);
        Assert.Equal(0, inventory.InspectionCount);
    }

    [Fact]
    public async Task FailedReauthenticationDoesNotInspectNetworkInterfaces()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = false };
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(authentication, inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "incorrect",
            ValidStaticRequest(inventory.Interface.Id),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Equal(
            NetworkConfigurationPreviewFailure.ReauthenticationFailed,
            rejected.Failure);
        Assert.Equal(1, authentication.VerificationCount);
        Assert.Equal(0, inventory.InspectionCount);
    }

    [Fact]
    public async Task MissingInterfaceFailsClosedAfterReauthentication()
    {
        var inventory = new NetworkConfigurationInventoryStub();
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            ValidStaticRequest("mac:aa:bb:cc:dd:ee:ff"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Equal(NetworkConfigurationPreviewFailure.InterfaceNotFound, rejected.Failure);
        Assert.Equal(1, inventory.InspectionCount);
    }

    [Fact]
    public async Task DuplicateInterfaceIdentityFailsClosed()
    {
        var inventory = new NetworkConfigurationInventoryStub
        {
            DuplicateIdentity = true
        };
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            ValidStaticRequest(inventory.Interface.Id),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Equal(NetworkConfigurationPreviewFailure.InterfaceNotFound, rejected.Failure);
    }

    [Fact]
    public async Task IncompleteInventorySnapshotFailsClosed()
    {
        var inventory = new NetworkConfigurationInventoryStub
        {
            Interface = new NetworkConfigurationInterfaceSnapshot(
                "mac:00:11:22:33:44:55",
                string.Empty,
                "dhcp",
                ["192.168.1.10/24"],
                "192.168.1.1")
        };
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            ValidStaticRequest(inventory.Interface.Id),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationPreviewRejected>(outcome);
        Assert.Equal(NetworkConfigurationPreviewFailure.InterfaceNotFound, rejected.Failure);
    }

    [Fact]
    public async Task ApplyRevalidatesAndForwardsOnlyNormalizedConfiguration()
    {
        var now = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var inventory = new NetworkConfigurationInventoryStub();
        var executor = new NetworkConfigurationExecutorStub();
        executor.ApplyHandler = (operationId, _, _, _, deadline) =>
            new NetworkConfigurationExecutionSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.AwaitingConfirmation,
                    deadline));
        var service = CreateService(
            authentication,
            inventory,
            executor,
            new FixedTimeProvider(now));

        var outcome = await service.ApplyAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                inventory.Interface.Id.ToUpperInvariant(),
                NetworkAddressingMode.StaticIpv4,
                " 192.168.1.20 ",
                " 255.255.255.0 ",
                " 192.168.1.1 "),
            CancellationToken.None);

        var succeeded = Assert.IsType<NetworkConfigurationCommandSucceeded>(outcome);
        Assert.Equal(
            NetworkConfigurationOperationState.AwaitingConfirmation,
            succeeded.Operation.State);
        Assert.Equal(now.AddMinutes(2), succeeded.Operation.ConfirmationDeadline);
        Assert.Equal("192.168.1.20", executor.AppliedConfiguration?.IpAddress);
        Assert.Equal(24, executor.AppliedConfiguration?.PrefixLength);
        Assert.Equal(1, authentication.VerificationCount);
        Assert.Equal(1, inventory.InspectionCount);
        Assert.Equal(1, executor.ApplyCount);
    }

    [Fact]
    public async Task InvalidApplyDoesNotReachTheExecutor()
    {
        var executor = new NetworkConfigurationExecutorStub();
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            new NetworkConfigurationInventoryStub(),
            executor);

        var outcome = await service.ApplyAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedNetworkConfiguration(
                "mac:00:11:22:33:44:55",
                NetworkAddressingMode.StaticIpv4,
                "192.168.1.20",
                "255.0.255.0",
                "192.168.1.1"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationCommandRejected>(outcome);
        Assert.Equal(NetworkConfigurationCommandFailure.InvalidConfiguration, rejected.Failure);
        Assert.Equal(0, executor.ApplyCount);
    }

    [Fact]
    public async Task ExecutorUnavailabilityFailsApplyClosed()
    {
        var executor = new NetworkConfigurationExecutorStub
        {
            ApplyOutcome = new NetworkConfigurationExecutionRejected(
                NetworkConfigurationExecutionFailure.Unavailable,
                "network.write_unavailable",
                Retryable: false)
        };
        var service = CreateService(
            new AuthenticationServiceStub { PasswordIsValid = true },
            new NetworkConfigurationInventoryStub(),
            executor);

        var outcome = await service.ApplyAsync(
            Guid.NewGuid(),
            "secret",
            ValidStaticRequest("mac:00:11:22:33:44:55"),
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationCommandRejected>(outcome);
        Assert.Equal(NetworkConfigurationCommandFailure.ExecutorUnavailable, rejected.Failure);
        Assert.Equal("network.write_unavailable", rejected.Code);
    }

    [Fact]
    public async Task ConfirmForwardsOperationAndUserIdentity()
    {
        var operationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var executor = new NetworkConfigurationExecutorStub
        {
            ConfirmOutcome = new NetworkConfigurationExecutionSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.Confirmed,
                    null))
        };
        var store = NetworkConfigurationOperationStoreStub.Awaiting(operationId, userId);
        var service = CreateService(
            new AuthenticationServiceStub(),
            new NetworkConfigurationInventoryStub(),
            executor,
            operationStore: store);

        var outcome = await service.ConfirmAsync(userId, operationId, CancellationToken.None);

        var succeeded = Assert.IsType<NetworkConfigurationCommandSucceeded>(outcome);
        Assert.Equal(NetworkConfigurationOperationState.Confirmed, succeeded.Operation.State);
        Assert.Equal(operationId, executor.LastOperationId);
        Assert.Equal(userId, executor.LastUserId);
    }

    [Fact]
    public async Task ConfirmDoesNotExposeAnotherUsersPendingOperation()
    {
        var operationId = Guid.NewGuid();
        var executor = new NetworkConfigurationExecutorStub();
        var store = NetworkConfigurationOperationStoreStub.Awaiting(
            operationId,
            Guid.NewGuid());
        var service = CreateService(
            new AuthenticationServiceStub(),
            new NetworkConfigurationInventoryStub(),
            executor,
            operationStore: store);

        var outcome = await service.ConfirmAsync(
            Guid.NewGuid(),
            operationId,
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationCommandRejected>(outcome);
        Assert.Equal(NetworkConfigurationCommandFailure.OperationNotFound, rejected.Failure);
        Assert.Equal(Guid.Empty, executor.LastOperationId);
    }

    [Fact]
    public async Task RollbackConflictIsReturnedWithoutChangingItsErrorCode()
    {
        var operationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var executor = new NetworkConfigurationExecutorStub
        {
            RollbackOutcome = new NetworkConfigurationExecutionRejected(
                NetworkConfigurationExecutionFailure.Conflict,
                "network.operation_not_awaiting_confirmation",
                Retryable: false)
        };
        var store = NetworkConfigurationOperationStoreStub.Awaiting(operationId, userId);
        var service = CreateService(
            new AuthenticationServiceStub(),
            new NetworkConfigurationInventoryStub(),
            executor,
            operationStore: store);

        var outcome = await service.RollbackAsync(
            userId,
            operationId,
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationCommandRejected>(outcome);
        Assert.Equal(NetworkConfigurationCommandFailure.Conflict, rejected.Failure);
        Assert.Equal("network.operation_not_awaiting_confirmation", rejected.Code);
    }

    [Fact]
    public async Task ConfirmRejectsAnExecutorThatSkipsTheExpectedStateTransition()
    {
        var operationId = Guid.NewGuid();
        var executor = new NetworkConfigurationExecutorStub
        {
            ConfirmOutcome = new NetworkConfigurationExecutionSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.RolledBack,
                    null))
        };
        var userId = Guid.NewGuid();
        var store = NetworkConfigurationOperationStoreStub.Awaiting(operationId, userId);
        var service = CreateService(
            new AuthenticationServiceStub(),
            new NetworkConfigurationInventoryStub(),
            executor,
            operationStore: store);

        var outcome = await service.ConfirmAsync(
            userId,
            operationId,
            CancellationToken.None);

        var rejected = Assert.IsType<NetworkConfigurationCommandRejected>(outcome);
        Assert.Equal(NetworkConfigurationCommandFailure.ExecutorRejected, rejected.Failure);
        Assert.Equal("network.operation_result_mismatch", rejected.Code);
        Assert.Equal(StoredNetworkConfigurationOperationState.Interrupted, store.State);
    }

    private static NetworkConfigurationService CreateService(
        IAuthenticationService authentication,
        INetworkConfigurationInventory inventory,
        INetworkConfigurationExecutor? executor = null,
        TimeProvider? timeProvider = null,
        INetworkConfigurationOperationStore? operationStore = null)
    {
        return new NetworkConfigurationService(
            authentication,
            inventory,
            executor ?? new NetworkConfigurationExecutorStub(),
            operationStore ?? new NetworkConfigurationOperationStoreStub(),
            timeProvider ?? TimeProvider.System);
    }

    private static RequestedNetworkConfiguration ValidStaticRequest(string interfaceId)
    {
        return new RequestedNetworkConfiguration(
            interfaceId,
            NetworkAddressingMode.StaticIpv4,
            "192.168.1.20",
            "255.255.255.0",
            "192.168.1.1");
    }

    private sealed class AuthenticationServiceStub : IAuthenticationService
    {
        public bool PasswordIsValid { get; init; }
        public int VerificationCount { get; private set; }

        public Task<bool> VerifyPasswordAsync(
            Guid userId,
            string password,
            CancellationToken cancellationToken)
        {
            VerificationCount++;
            return Task.FromResult(PasswordIsValid);
        }

        public Task<SignInOutcome> SignInAdministratorAsync(
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticatedUser?> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PasswordChangeOutcome> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NetworkConfigurationInventoryStub : INetworkConfigurationInventory
    {
        public NetworkConfigurationInterfaceSnapshot Interface { get; init; } = new(
            "mac:00:11:22:33:44:55",
            "enp1s0",
            "dhcp",
            ["192.168.1.10/24"],
            "192.168.1.1");

        public int InspectionCount { get; private set; }
        public bool DuplicateIdentity { get; init; }

        public Task<IReadOnlyList<NetworkConfigurationInterfaceSnapshot>> InspectInterfacesAsync(
            CancellationToken cancellationToken)
        {
            InspectionCount++;
            return Task.FromResult<IReadOnlyList<NetworkConfigurationInterfaceSnapshot>>(
                DuplicateIdentity
                    ? [Interface, Interface with { Name = "enp2s0" }]
                    : [Interface]);
        }
    }

    private sealed class NetworkConfigurationExecutorStub : INetworkConfigurationExecutor
    {
        public NetworkConfigurationExecutionOutcome? ApplyOutcome { get; init; }
        public NetworkConfigurationExecutionOutcome? ConfirmOutcome { get; init; }
        public NetworkConfigurationExecutionOutcome? RollbackOutcome { get; init; }
        public Func<
            Guid,
            Guid,
            string,
            NormalizedNetworkConfiguration,
            DateTimeOffset,
            NetworkConfigurationExecutionOutcome>?
            ApplyHandler
        { get; set; }

        public int ApplyCount { get; private set; }
        public Guid LastOperationId { get; private set; }
        public Guid LastUserId { get; private set; }
        public NormalizedNetworkConfiguration? AppliedConfiguration { get; private set; }

        public Task<NetworkConfigurationExecutionOutcome> ApplyAsync(
            Guid operationId,
            Guid userId,
            string interfaceId,
            NormalizedNetworkConfiguration configuration,
            DateTimeOffset confirmationDeadline,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            LastOperationId = operationId;
            LastUserId = userId;
            AppliedConfiguration = configuration;
            return Task.FromResult(
                ApplyHandler?.Invoke(
                    operationId,
                    userId,
                    interfaceId,
                    configuration,
                    confirmationDeadline)
                ?? ApplyOutcome
                ?? Unavailable());
        }

        public Task<NetworkConfigurationExecutionOutcome> ConfirmAsync(
            Guid operationId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastOperationId = operationId;
            LastUserId = userId;
            return Task.FromResult(ConfirmOutcome ?? Unavailable());
        }

        public Task<NetworkConfigurationExecutionOutcome> RollbackAsync(
            Guid operationId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastOperationId = operationId;
            LastUserId = userId;
            return Task.FromResult(RollbackOutcome ?? Unavailable());
        }

        private static NetworkConfigurationExecutionOutcome Unavailable()
        {
            return new NetworkConfigurationExecutionRejected(
                NetworkConfigurationExecutionFailure.Unavailable,
                "network.write_unavailable",
                Retryable: false);
        }
    }

    private sealed class NetworkConfigurationOperationStoreStub :
        INetworkConfigurationOperationStore
    {
        private StoredNetworkConfigurationOperation? operation;
        public StoredNetworkConfigurationOperationState? State => operation?.State;

        public static NetworkConfigurationOperationStoreStub Awaiting(
            Guid operationId,
            Guid userId)
        {
            return new NetworkConfigurationOperationStoreStub
            {
                operation = new StoredNetworkConfigurationOperation(
                    operationId,
                    userId,
                    "mac:00:11:22:33:44:55",
                    new NormalizedNetworkConfiguration(
                        NetworkAddressingMode.StaticIpv4,
                        "192.168.1.20",
                        "255.255.255.0",
                        24,
                        "192.168.1.1"),
                    StoredNetworkConfigurationOperationState.AwaitingConfirmation,
                    DateTimeOffset.UtcNow.AddMinutes(2),
                    null)
            };
        }

        public Task<NetworkConfigurationOperationStartOutcome> StartAsync(
            Guid userId,
            string interfaceId,
            NormalizedNetworkConfiguration requested,
            DateTimeOffset confirmationDeadline,
            CancellationToken cancellationToken)
        {
            operation = new StoredNetworkConfigurationOperation(
                Guid.NewGuid(),
                userId,
                interfaceId,
                requested,
                StoredNetworkConfigurationOperationState.Applying,
                confirmationDeadline,
                null);
            return Task.FromResult<NetworkConfigurationOperationStartOutcome>(
                new NetworkConfigurationOperationStarted(operation));
        }

        public Task<StoredNetworkConfigurationOperation?> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                operation?.Id == operationId ? operation : null);
        }

        public Task RecordAsync(
            Guid operationId,
            StoredNetworkConfigurationOperationState state,
            string? errorCode,
            bool releaseLock,
            CancellationToken cancellationToken)
        {
            if (operation?.Id == operationId)
            {
                operation = operation with { State = state, ErrorCode = errorCode };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
