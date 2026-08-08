//--------------------------//
//--------验证 RAID 两阶段预检、状态复核和 Operation 所有权---------//
//--------Verifies RAID two-phase previews, state rechecks, and operation ownership--------//
//-------------------------//
using Nas.Application.Authentication;
using Nas.Application.RaidManagement;
using Nas.Application.Storage;
using Nas.Domain.Operations;
using Nas.Infrastructure.Privileged;

namespace Nas.Api.Tests;

public sealed class RaidManagementServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatePreviewRejectsASystemDiskWithoutIssuingAToken()
    {
        var inventory = new StorageInventoryStub
        {
            Disks = [Disk("wwn:system", systemDevice: true), Disk("wwn:data")]
        };
        var service = CreateService(inventory: inventory);

        var outcome = await service.CreatePreviewAsync(
            Guid.NewGuid(),
            "secret",
            new RequestedRaidOperation(
                RaidOperationKind.Create,
                null,
                "data",
                "raid1",
                ["wwn:system", "wwn:data"],
                null,
                null),
            CancellationToken.None);

        var preview = Assert.IsType<RaidPreviewCreated>(outcome).Preview;
        Assert.False(preview.CanExecute);
        Assert.Null(preview.PreviewToken);
        Assert.Contains(preview.BlockingIssues, issue => issue.Code == "raid.device_busy");
    }

    [Fact]
    public async Task ValidPreviewIsOneTimeAndExecutesOnlyAfterASecondReauthentication()
    {
        var userId = Guid.NewGuid();
        var authentication = new AuthenticationStub();
        var inventory = new StorageInventoryStub
        {
            Disks = [Disk("wwn:a"), Disk("wwn:b")]
        };
        var executor = new ExecutorStub();
        var service = CreateService(authentication, inventory, executor: executor);
        var request = new RequestedRaidOperation(
            RaidOperationKind.Create,
            null,
            "data",
            "raid1",
            ["wwn:a", "wwn:b"],
            null,
            null);

        var preview = Assert.IsType<RaidPreviewCreated>(await service.CreatePreviewAsync(
            userId,
            "secret",
            request,
            CancellationToken.None)).Preview;
        Assert.True(preview.CanExecute);
        Assert.NotNull(preview.PreviewToken);

        var executed = await service.ExecuteAsync(
            userId,
            "secret",
            preview.PreviewToken!,
            preview.ConfirmationPhrase,
            "create-data-1",
            CancellationToken.None);

        var operation = Assert.IsType<RaidCommandSucceeded>(executed).Operation;
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(2, authentication.VerificationCount);
        Assert.Equal(1, executor.ExecutionCount);

        var replay = await service.ExecuteAsync(
            userId,
            "secret",
            preview.PreviewToken!,
            preview.ConfirmationPhrase,
            "create-data-1",
            CancellationToken.None);
        Assert.Equal(RaidCommandFailure.PreviewExpired, Assert.IsType<RaidCommandRejected>(replay).Failure);
        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public async Task ChangedDiskStateInvalidatesThePreviewBeforeTheExecutor()
    {
        var userId = Guid.NewGuid();
        var inventory = new StorageInventoryStub
        {
            Disks = [Disk("wwn:a"), Disk("wwn:b")]
        };
        var executor = new ExecutorStub();
        var service = CreateService(inventory: inventory, executor: executor);
        var preview = Assert.IsType<RaidPreviewCreated>(await service.CreatePreviewAsync(
            userId,
            "secret",
            new RequestedRaidOperation(
                RaidOperationKind.Create,
                null,
                "data",
                "raid1",
                ["wwn:a", "wwn:b"],
                null,
                null),
            CancellationToken.None)).Preview;
        inventory.Disks = [Disk("wwn:a", inUse: true), Disk("wwn:b")];

        var outcome = await service.ExecuteAsync(
            userId,
            "secret",
            preview.PreviewToken!,
            preview.ConfirmationPhrase,
            "create-data-2",
            CancellationToken.None);

        Assert.Equal(RaidCommandFailure.StateChanged, Assert.IsType<RaidCommandRejected>(outcome).Failure);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task OperationLookupDoesNotExposeAnotherUsersOperation()
    {
        var owner = Guid.NewGuid();
        var store = new OperationStoreStub
        {
            Existing = new RaidOperation(
                Guid.NewGuid(),
                owner,
                RaidOperationKind.Delete,
                DeleteRequest(),
                OperationStatus.Succeeded,
                "array:md:test",
                "delete-1",
                1,
                "md:test",
                null,
                false,
                100,
                Now,
                Now)
        };
        var service = CreateService(operations: store);

        var outcome = await service.GetOperationAsync(
            Guid.NewGuid(),
            store.Existing.Id,
            CancellationToken.None);

        Assert.Equal(
            RaidCommandFailure.OperationNotFound,
            Assert.IsType<RaidCommandRejected>(outcome).Failure);
    }

    [Fact]
    public async Task InterruptedDeleteIsReconciledAsSucceededWhenTheArrayIsGone()
    {
        var userId = Guid.NewGuid();
        var store = new OperationStoreStub
        {
            Existing = new RaidOperation(
                Guid.NewGuid(),
                userId,
                RaidOperationKind.Delete,
                DeleteRequest(),
                OperationStatus.Interrupted,
                "array:md:test",
                "delete-uncertain",
                2,
                "md:test",
                "privileged.unavailable",
                true,
                null,
                Now,
                Now)
        };
        var service = CreateService(
            inventory: new StorageInventoryStub(),
            operations: store);

        var outcome = await service.GetOperationAsync(
            userId,
            store.Existing.Id,
            CancellationToken.None);

        Assert.Equal(
            OperationStatus.Succeeded,
            Assert.IsType<RaidCommandSucceeded>(outcome).Operation.Status);
        Assert.True(store.ReleasedLocks);
    }

    private static RaidManagementService CreateService(
        AuthenticationStub? authentication = null,
        StorageInventoryStub? inventory = null,
        OperationStoreStub? operations = null,
        ExecutorStub? executor = null)
    {
        var time = new FixedTimeProvider(Now);
        return new RaidManagementService(
            authentication ?? new AuthenticationStub(),
            inventory ?? new StorageInventoryStub(),
            new InMemoryRaidPreviewStore(time),
            operations ?? new OperationStoreStub(),
            executor ?? new ExecutorStub(),
            time);
    }

    private static BlockDeviceInformation Disk(
        string id,
        bool systemDevice = false,
        bool inUse = false) => new(
            id,
            Stable: true,
            IdentityConflict: false,
            TopologyComplete: true,
            Name: id.Replace("wwn:", "sd", StringComparison.Ordinal),
            Path: $"/dev/{id.Replace("wwn:", "sd", StringComparison.Ordinal)}",
            Model: "Test Disk",
            SerialNumber: id,
            Wwn: id,
            SizeBytes: 1024 * 1024,
            LogicalSectorBytes: 512,
            PhysicalSectorBytes: 4096,
            Rotational: false,
            Removable: false,
            ReadOnly: false,
            Partitions: [],
            MountPoints: [],
            SystemDevice: systemDevice,
            Swap: false,
            RaidMember: false,
            InUse: inUse,
            DependentDevices: []);

    private sealed class AuthenticationStub : IAuthenticationService
    {
        public int VerificationCount { get; private set; }

        public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
        {
            VerificationCount++;
            return Task.FromResult(password == "secret");
        }

        public Task<SignInOutcome> SignInAdministratorAsync(string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AuthenticatedUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PasswordChangeOutcome> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StorageInventoryStub : IStorageInventoryClient
    {
        public IReadOnlyList<BlockDeviceInformation> Disks { get; set; } = [];
        public IReadOnlyList<RaidArrayInformation> Arrays { get; set; } = [];

        public Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Disks);
        public Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Arrays);
    }

    private sealed class ExecutorStub : IRaidCommandExecutor
    {
        public int ExecutionCount { get; private set; }

        public Task<RaidExecutionOutcome> ExecuteAsync(RaidExecutionCommand command, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult<RaidExecutionOutcome>(
                new RaidExecutionAccepted("md:created", InProgress: false, ProgressPercentage: 100));
        }
    }

    private sealed class OperationStoreStub : IRaidOperationStore
    {
        public RaidOperation? Existing { get; set; }
        public bool ReleasedLocks { get; private set; }

        public Task<RaidOperationStartOutcome> StartAsync(
            Guid userId,
            RaidPreviewTicket ticket,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            Existing = new RaidOperation(
                Guid.NewGuid(),
                userId,
                ticket.Requested.Kind,
                ticket.Requested,
                OperationStatus.Running,
                ticket.ResourceIds[0],
                idempotencyKey,
                1,
                ticket.Requested.ArrayId,
                null,
                false,
                null,
                Now,
                Now);
            return Task.FromResult<RaidOperationStartOutcome>(new RaidOperationStarted(Existing));
        }

        public Task<RaidOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Existing?.Id == operationId ? Existing : null);

        public Task<RaidOperation> RecordExecutionAsync(
            Guid operationId,
            OperationStatus status,
            string? arrayId,
            string? errorCode,
            bool retryable,
            int? progressPercentage,
            bool releaseLocks,
            CancellationToken cancellationToken)
        {
            ReleasedLocks = releaseLocks;
            Existing = Existing! with
            {
                Status = status,
                ArrayId = arrayId ?? Existing!.ArrayId,
                ErrorCode = errorCode,
                Retryable = retryable,
                ProgressPercentage = progressPercentage,
                UpdatedAt = Now
            };
            return Task.FromResult(Existing);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static RequestedRaidOperation DeleteRequest() => new(
        RaidOperationKind.Delete,
        "md:test",
        null,
        null,
        [],
        null,
        null);
}
