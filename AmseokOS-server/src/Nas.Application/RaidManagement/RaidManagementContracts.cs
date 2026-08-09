//--------------------------//
//--------定义 RAID 生命周期预检、操作与外部执行边界---------//
//--------Defines RAID lifecycle previews, operations, and execution boundaries--------//
//-------------------------//
using Nas.Domain.Operations;

namespace Nas.Application.RaidManagement;

public enum RaidOperationKind
{
    Create,
    Delete,
    AddDevice,
    RemoveDevice,
    ReplaceDevice,
    Grow,
    Shrink
}

public sealed record RequestedRaidOperation(
    RaidOperationKind Kind,
    string? ArrayId,
    string? ArrayName,
    string? Level,
    IReadOnlyList<string> DeviceIds,
    string? SourceDeviceId,
    int? TargetDeviceCount);

public sealed record RaidOperationIssue(string Code, string Field, string Message);

public sealed record RaidOperationPreview(
    RequestedRaidOperation Requested,
    string? ArrayDisplayName,
    IReadOnlyList<string> ExpectedMemberDeviceIds,
    bool CanExecute,
    string? PreviewToken,
    DateTimeOffset? ExpiresAt,
    string ConfirmationPhrase,
    IReadOnlyList<RaidOperationIssue> BlockingIssues,
    IReadOnlyList<string> Warnings);

public enum RaidPreviewFailure
{
    InvalidRequest,
    ReauthenticationFailed
}

public abstract record RaidPreviewOutcome;
public sealed record RaidPreviewCreated(RaidOperationPreview Preview) : RaidPreviewOutcome;
public sealed record RaidPreviewRejected(
    RaidPreviewFailure Failure,
    IReadOnlyList<RaidOperationIssue> Issues) : RaidPreviewOutcome;

public sealed record RaidPreviewTicket(
    string Token,
    Guid UserId,
    RequestedRaidOperation Requested,
    string? ArrayDisplayName,
    IReadOnlyList<string> ExpectedMemberDeviceIds,
    IReadOnlyList<string> ResourceIds,
    string SnapshotFingerprint,
    string ConfirmationPhrase,
    DateTimeOffset ExpiresAt);

public sealed record RaidOperation(
    Guid Id,
    Guid UserId,
    RaidOperationKind Kind,
    RequestedRaidOperation Requested,
    OperationStatus Status,
    string ResourceId,
    string IdempotencyKey,
    long FencingToken,
    string? ArrayId,
    string? ErrorCode,
    bool Retryable,
    int? ProgressPercentage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public enum RaidCommandFailure
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

public abstract record RaidCommandOutcome;
public sealed record RaidCommandSucceeded(RaidOperation Operation) : RaidCommandOutcome;
public sealed record RaidCommandRejected(
    RaidCommandFailure Failure,
    string Code,
    bool Retryable,
    IReadOnlyList<RaidOperationIssue> Issues) : RaidCommandOutcome;

public sealed record RaidExecutionCommand(
    Guid OperationId,
    string IdempotencyKey,
    long FencingToken,
    RequestedRaidOperation Requested,
    IReadOnlyList<string> ExpectedMemberDeviceIds,
    string SnapshotFingerprint);

public abstract record RaidExecutionOutcome;
public sealed record RaidExecutionAccepted(
    string? ArrayId,
    bool InProgress,
    int? ProgressPercentage) : RaidExecutionOutcome;
public sealed record RaidExecutionRejected(
    string Code,
    bool Retryable,
    bool OutcomeUncertain) : RaidExecutionOutcome;

public abstract record RaidOperationStartOutcome;
public sealed record RaidOperationStarted(RaidOperation Operation) : RaidOperationStartOutcome;
public sealed record RaidOperationAlreadyExists(RaidOperation Operation) : RaidOperationStartOutcome;
public sealed record RaidOperationStartRejected(string Code) : RaidOperationStartOutcome;

public interface IRaidManagementService
{
    Task<RaidPreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedRaidOperation requested,
        CancellationToken cancellationToken);

    Task<RaidCommandOutcome> ExecuteAsync(
        Guid userId,
        string password,
        string previewToken,
        string confirmationPhrase,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<RaidCommandOutcome> GetOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken);
}

public interface IRaidPreviewStore
{
    RaidPreviewTicket Store(
        Guid userId,
        RequestedRaidOperation requested,
        string? arrayDisplayName,
        IReadOnlyList<string> expectedMemberDeviceIds,
        IReadOnlyList<string> resourceIds,
        string snapshotFingerprint,
        string confirmationPhrase,
        DateTimeOffset expiresAt);

    RaidPreviewTicket? Consume(Guid userId, string token, DateTimeOffset now);
}

public interface IRaidOperationStore
{
    Task<RaidOperationStartOutcome> StartAsync(
        Guid userId,
        RaidPreviewTicket ticket,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<RaidOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken);

    Task<RaidOperation> RecordExecutionAsync(
        Guid operationId,
        OperationStatus status,
        string? arrayId,
        string? errorCode,
        bool retryable,
        int? progressPercentage,
        bool releaseLocks,
        CancellationToken cancellationToken);
}

public interface IRaidCommandExecutor
{
    Task<RaidExecutionOutcome> ExecuteAsync(
        RaidExecutionCommand command,
        CancellationToken cancellationToken);
}
