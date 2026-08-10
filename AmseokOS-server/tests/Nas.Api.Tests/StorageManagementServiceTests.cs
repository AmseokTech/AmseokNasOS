//--------------------------//
//--------验证数据卷供应、共享预检、状态复核与 Operation 所有权---------//
//--------Verifies volume/share previews, state checks, and operation ownership--------//
//-------------------------//
using Nas.Application.Authentication;
using Nas.Application.Storage;
using Nas.Application.StorageManagement;
using Nas.Domain.Operations;
using Nas.Infrastructure.Privileged;

namespace Nas.Api.Tests;

public sealed class StorageManagementServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProvisionPreviewRequiresAHealthyIdleStableArray()
    {
        var inventory = new InventoryStub
        {
            Arrays = [Array(degraded: 1)]
        };
        var preview = Assert.IsType<StoragePreviewCreated>(
            await CreateService(inventory: inventory).CreatePreviewAsync(
                Guid.NewGuid(),
                "secret",
                ProvisionRequest(),
                CancellationToken.None)).Preview;

        Assert.False(preview.CanExecute);
        Assert.Contains(
            preview.BlockingIssues,
            issue => issue.Code == "storage.array_not_ready");
    }

    [Fact]
    public async Task ProvisionRunsOnlyAfterSecondAuthenticationAndReturnsMountedVolume()
    {
        var authentication = new AuthenticationStub();
        var executor = new ExecutorStub();
        var service = CreateService(authentication, executor: executor);
        var userId = Guid.NewGuid();
        var preview = Assert.IsType<StoragePreviewCreated>(
            await service.CreatePreviewAsync(
                userId,
                "secret",
                ProvisionRequest(),
                CancellationToken.None)).Preview;

        var outcome = await service.ExecuteAsync(
            userId,
            "secret",
            preview.PreviewToken!,
            preview.ConfirmationPhrase,
            "provision-data-1",
            CancellationToken.None);

        var operation = Assert.IsType<StorageCommandSucceeded>(outcome).Operation;
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.True(operation.Volume!.Mounted);
        Assert.True(operation.Volume.ReadWriteVerified);
        Assert.Equal(2, authentication.VerificationCount);
        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public async Task SharePreviewRejectsAnUnscopedClientNetwork()
    {
        var volume = Volume();
        var request = new RequestedStorageOperation(
            StorageOperationKind.ConfigureShares,
            null,
            volume.Id,
            null,
            null,
            null,
            null,
            new SmbShareSettings(true, "data", false, false, "0.0.0.0/0"),
            new NfsShareSettings(true, "0.0.0.0/0", false));
        var preview = Assert.IsType<StoragePreviewCreated>(
            await CreateService(volumes: new VolumeClientStub { Volumes = [volume] })
                .CreatePreviewAsync(Guid.NewGuid(), "secret", request, CancellationToken.None)).Preview;

        Assert.False(preview.CanExecute);
        Assert.Contains(preview.BlockingIssues, issue => issue.Code == "storage.smb_network_invalid");
        Assert.Contains(preview.BlockingIssues, issue => issue.Code == "storage.nfs_network_invalid");
    }

    [Fact]
    public async Task ChangedArrayStateInvalidatesAProvisionPreview()
    {
        var inventory = new InventoryStub { Arrays = [Array()] };
        var executor = new ExecutorStub();
        var service = CreateService(inventory: inventory, executor: executor);
        var userId = Guid.NewGuid();
        var preview = Assert.IsType<StoragePreviewCreated>(
            await service.CreatePreviewAsync(
                userId,
                "secret",
                ProvisionRequest(),
                CancellationToken.None)).Preview;
        inventory.Arrays = [Array(syncAction: "resync")];

        var outcome = await service.ExecuteAsync(
            userId,
            "secret",
            preview.PreviewToken!,
            preview.ConfirmationPhrase,
            "provision-data-2",
            CancellationToken.None);

        Assert.Equal(
            StorageCommandFailure.StateChanged,
            Assert.IsType<StorageCommandRejected>(outcome).Failure);
        Assert.Equal(0, executor.ExecutionCount);
    }

    private static StorageManagementService CreateService(
        AuthenticationStub? authentication = null,
        InventoryStub? inventory = null,
        VolumeClientStub? volumes = null,
        OperationStoreStub? operations = null,
        ExecutorStub? executor = null)
    {
        var time = new FixedTimeProvider(Now);
        return new StorageManagementService(
            authentication ?? new AuthenticationStub(),
            inventory ?? new InventoryStub { Arrays = [Array()] },
            volumes ?? new VolumeClientStub(),
            new InMemoryStoragePreviewStore(time),
            operations ?? new OperationStoreStub(),
            executor ?? new ExecutorStub(),
            time);
    }

    private static RequestedStorageOperation ProvisionRequest() => new(
        StorageOperationKind.ProvisionVolume,
        "md:test",
        null,
        "data",
        "root",
        "amseoknas-data",
        "0770",
        new SmbShareSettings(false, null, true, false, null),
        new NfsShareSettings(false, null, true));

    private static RaidArrayInformation Array(
        long degraded = 0,
        string syncAction = "idle") => new(
        "md:test",
        "data",
        "/dev/md0",
        "699212ff:e1e67804:7d4ca124:4b014bcf",
        "raid1",
        "active",
        "1.2",
        1024 * 1024,
        2,
        degraded,
        syncAction,
        null,
        null,
        []);

    private static ManagedVolumeInformation Volume() => new(
        "volume:01234567-89ab-cdef-0123-456789abcdef",
        "data",
        "md:test",
        "/dev/md0",
        "01234567-89ab-cdef-0123-456789abcdef",
        "ext4",
        "/srv/amseoknas/volumes/data",
        true,
        true,
        "root",
        "amseoknas-data",
        "0770",
        true,
        new SmbShareSettings(false, null, true, false, null),
        new NfsShareSettings(false, null, true));

    private sealed class AuthenticationStub : IAuthenticationService
    {
        public int VerificationCount { get; private set; }
        public Task<bool> VerifyPasswordAsync(
            Guid userId,
            string password,
            CancellationToken cancellationToken)
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

    private sealed class InventoryStub : IStorageInventoryClient
    {
        public IReadOnlyList<RaidArrayInformation> Arrays { get; set; } = [];
        public Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Arrays);
        public Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockDeviceInformation>>([]);
    }

    private sealed class VolumeClientStub : IStorageManagementClient
    {
        public IReadOnlyList<ManagedVolumeInformation> Volumes { get; set; } = [];
        public Task<IReadOnlyList<ManagedVolumeInformation>> GetManagedVolumesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Volumes);
    }

    private sealed class ExecutorStub : IStorageCommandExecutor
    {
        public int ExecutionCount { get; private set; }
        public Task<StorageExecutionOutcome> ExecuteAsync(
            StorageExecutionCommand command,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult<StorageExecutionOutcome>(new StorageExecutionAccepted(Volume()));
        }
    }

    private sealed class OperationStoreStub : IStorageOperationStore
    {
        private StorageOperation? operation;

        public Task<StorageOperationStartOutcome> StartAsync(
            Guid userId,
            StoragePreviewTicket ticket,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            operation = new StorageOperation(
                Guid.NewGuid(),
                userId,
                ticket.Requested.Kind,
                ticket.Requested,
                OperationStatus.Running,
                ticket.ResourceIds[0],
                idempotencyKey,
                1,
                null,
                null,
                false,
                Now,
                Now);
            return Task.FromResult<StorageOperationStartOutcome>(
                new StorageOperationStarted(operation));
        }

        public Task<StorageOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(operation?.Id == operationId ? operation : null);

        public Task<StorageOperation> RecordExecutionAsync(
            Guid operationId,
            OperationStatus status,
            ManagedVolumeInformation? volume,
            string? errorCode,
            bool retryable,
            bool releaseLocks,
            CancellationToken cancellationToken)
        {
            operation = operation! with
            {
                Status = status,
                Volume = volume ?? operation!.Volume,
                ErrorCode = errorCode,
                Retryable = retryable,
                UpdatedAt = Now
            };
            return Task.FromResult(operation);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
