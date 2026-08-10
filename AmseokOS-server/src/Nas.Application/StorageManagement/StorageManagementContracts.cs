//--------------------------//
//--------定义数据卷供应、权限、读写校验与共享管理边界---------//
//--------Defines volume provisioning, permissions, verification, and share boundaries--------//
//-------------------------//
using Nas.Domain.Operations;

namespace Nas.Application.StorageManagement;

public enum StorageOperationKind
{
    ProvisionVolume,
    UpdatePermissions,
    ConfigureShares,
    VerifyReadWrite
}

public sealed record SmbShareSettings(
    bool Enabled,
    string? ShareName,
    bool ReadOnly,
    bool GuestAccess,
    string? AllowedNetwork);

public sealed record NfsShareSettings(
    bool Enabled,
    string? ClientNetwork,
    bool ReadOnly);

public sealed record RequestedStorageOperation(
    StorageOperationKind Kind,
    string? ArrayId,
    string? VolumeId,
    string? VolumeName,
    string? OwnerName,
    string? GroupName,
    string? DirectoryMode,
    SmbShareSettings? Smb,
    NfsShareSettings? Nfs);

public sealed record ManagedVolumeInformation(
    string Id,
    string Name,
    string ArrayId,
    string ArrayPath,
    string FileSystemUuid,
    string FileSystemType,
    string MountPath,
    bool Mounted,
    bool PersistentMountEnabled,
    string OwnerName,
    string GroupName,
    string DirectoryMode,
    bool ReadWriteVerified,
    SmbShareSettings Smb,
    NfsShareSettings Nfs);

public sealed record StorageOperationIssue(string Code, string Field, string Message);

public sealed record StorageOperationPreview(
    RequestedStorageOperation Requested,
    ManagedVolumeInformation? ExistingVolume,
    bool CanExecute,
    string? PreviewToken,
    DateTimeOffset? ExpiresAt,
    string ConfirmationPhrase,
    IReadOnlyList<StorageOperationIssue> BlockingIssues,
    IReadOnlyList<string> Warnings);

public enum StoragePreviewFailure
{
    InvalidRequest,
    ReauthenticationFailed
}

public abstract record StoragePreviewOutcome;
public sealed record StoragePreviewCreated(StorageOperationPreview Preview) : StoragePreviewOutcome;
public sealed record StoragePreviewRejected(
    StoragePreviewFailure Failure,
    IReadOnlyList<StorageOperationIssue> Issues) : StoragePreviewOutcome;

public sealed record StoragePreviewTicket(
    string Token,
    Guid UserId,
    RequestedStorageOperation Requested,
    IReadOnlyList<string> ResourceIds,
    string SnapshotFingerprint,
    string ConfirmationPhrase,
    DateTimeOffset ExpiresAt);

public sealed record StorageOperation(
    Guid Id,
    Guid UserId,
    StorageOperationKind Kind,
    RequestedStorageOperation Requested,
    OperationStatus Status,
    string ResourceId,
    string IdempotencyKey,
    long FencingToken,
    ManagedVolumeInformation? Volume,
    string? ErrorCode,
    bool Retryable,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public enum StorageCommandFailure
{
    InvalidRequest,
    ReauthenticationFailed,
    PreviewExpired,
    ConfirmationMismatch,
    StateChanged,
    Conflict,
    OperationNotFound,
    ExecutorUnavailable,
    ExecutorRejected
}

public abstract record StorageCommandOutcome;
public sealed record StorageCommandSucceeded(StorageOperation Operation) : StorageCommandOutcome;
public sealed record StorageCommandRejected(
    StorageCommandFailure Failure,
    string Code,
    bool Retryable,
    IReadOnlyList<StorageOperationIssue> Issues) : StorageCommandOutcome;

public sealed record StorageExecutionCommand(
    Guid OperationId,
    string IdempotencyKey,
    long FencingToken,
    RequestedStorageOperation Requested,
    string SnapshotFingerprint);

public abstract record StorageExecutionOutcome;
public sealed record StorageExecutionAccepted(ManagedVolumeInformation Volume)
    : StorageExecutionOutcome;
public sealed record StorageExecutionRejected(
    string Code,
    bool Retryable,
    bool OutcomeUncertain) : StorageExecutionOutcome;

public abstract record StorageOperationStartOutcome;
public sealed record StorageOperationStarted(StorageOperation Operation)
    : StorageOperationStartOutcome;
public sealed record StorageOperationAlreadyExists(StorageOperation Operation)
    : StorageOperationStartOutcome;
public sealed record StorageOperationStartRejected(string Code)
    : StorageOperationStartOutcome;

public interface IStorageManagementService
{
    Task<IReadOnlyList<ManagedVolumeInformation>> GetVolumesAsync(
        CancellationToken cancellationToken);

    Task<StoragePreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedStorageOperation requested,
        CancellationToken cancellationToken);

    Task<StorageCommandOutcome> ExecuteAsync(
        Guid userId,
        string password,
        string previewToken,
        string confirmationPhrase,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StorageCommandOutcome> GetOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken);
}

public interface IStoragePreviewStore
{
    StoragePreviewTicket Store(
        Guid userId,
        RequestedStorageOperation requested,
        IReadOnlyList<string> resourceIds,
        string snapshotFingerprint,
        string confirmationPhrase,
        DateTimeOffset expiresAt);

    StoragePreviewTicket? Consume(Guid userId, string token, DateTimeOffset now);
}

public interface IStorageOperationStore
{
    Task<StorageOperationStartOutcome> StartAsync(
        Guid userId,
        StoragePreviewTicket ticket,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StorageOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken);

    Task<StorageOperation> RecordExecutionAsync(
        Guid operationId,
        OperationStatus status,
        ManagedVolumeInformation? volume,
        string? errorCode,
        bool retryable,
        bool releaseLocks,
        CancellationToken cancellationToken);
}

public interface IStorageManagementClient
{
    Task<IReadOnlyList<ManagedVolumeInformation>> GetManagedVolumesAsync(
        CancellationToken cancellationToken);
}

public interface IStorageCommandExecutor
{
    Task<StorageExecutionOutcome> ExecuteAsync(
        StorageExecutionCommand command,
        CancellationToken cancellationToken);
}
