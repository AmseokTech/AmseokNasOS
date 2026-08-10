//--------------------------//
//--------定义数据卷、权限、校验及 SMB/NFS 管理 HTTP 契约---------//
//--------Defines volume, permission, verification, and share HTTP contracts--------//
//-------------------------//
using System.ComponentModel.DataAnnotations;
using Nas.Application.StorageManagement;

namespace Nas.Api.Contracts;

public sealed record SmbShareSettingsRequest(
    bool Enabled,
    [param: MaxLength(32)] string? ShareName,
    bool ReadOnly,
    bool GuestAccess,
    [param: MaxLength(43)] string? AllowedNetwork);

public sealed record NfsShareSettingsRequest(
    bool Enabled,
    [param: MaxLength(43)] string? ClientNetwork,
    bool ReadOnly);

public sealed record CreateStorageOperationPreviewRequest(
    [param: Required, MaxLength(32)] string Action,
    [param: MaxLength(300)] string? ArrayId,
    [param: MaxLength(300)] string? VolumeId,
    [param: MaxLength(32)] string? VolumeName,
    [param: MaxLength(32)] string? OwnerName,
    [param: MaxLength(32)] string? GroupName,
    [param: MaxLength(4)] string? DirectoryMode,
    SmbShareSettingsRequest? Smb,
    NfsShareSettingsRequest? Nfs,
    [param: Required, MaxLength(256)] string Password);

public sealed record ExecuteStorageOperationRequest(
    [param: Required, MaxLength(256)] string PreviewToken,
    [param: Required, MaxLength(256)] string ConfirmationPhrase,
    [param: Required, MaxLength(200)] string IdempotencyKey,
    [param: Required, MaxLength(256)] string Password);

public sealed record StorageOperationIssueResponse(string Code, string Field, string Message)
{
    public static StorageOperationIssueResponse From(StorageOperationIssue issue) =>
        new(issue.Code, issue.Field, issue.Message);
}

public sealed record ManagedVolumeResponse(
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
    NfsShareSettings Nfs)
{
    public static ManagedVolumeResponse From(ManagedVolumeInformation volume) =>
        new(
            volume.Id,
            volume.Name,
            volume.ArrayId,
            volume.ArrayPath,
            volume.FileSystemUuid,
            volume.FileSystemType,
            volume.MountPath,
            volume.Mounted,
            volume.PersistentMountEnabled,
            volume.OwnerName,
            volume.GroupName,
            volume.DirectoryMode,
            volume.ReadWriteVerified,
            volume.Smb,
            volume.Nfs);
}

public sealed record StorageOperationPreviewResponse(
    string Action,
    RequestedStorageOperation Requested,
    ManagedVolumeResponse? ExistingVolume,
    bool CanExecute,
    string? PreviewToken,
    DateTimeOffset? ExpiresAt,
    string ConfirmationPhrase,
    IReadOnlyList<StorageOperationIssueResponse> BlockingIssues,
    IReadOnlyList<string> Warnings)
{
    public static StorageOperationPreviewResponse From(StorageOperationPreview preview) =>
        new(
            ActionName(preview.Requested.Kind),
            preview.Requested,
            preview.ExistingVolume is null ? null : ManagedVolumeResponse.From(preview.ExistingVolume),
            preview.CanExecute,
            preview.PreviewToken,
            preview.ExpiresAt,
            preview.ConfirmationPhrase,
            preview.BlockingIssues.Select(StorageOperationIssueResponse.From).ToArray(),
            preview.Warnings);

    public static string ActionName(StorageOperationKind kind) => kind switch
    {
        StorageOperationKind.ProvisionVolume => "provisionVolume",
        StorageOperationKind.UpdatePermissions => "updatePermissions",
        StorageOperationKind.ConfigureShares => "configureShares",
        StorageOperationKind.VerifyReadWrite => "verifyReadWrite",
        _ => throw new InvalidOperationException("Unknown storage action")
    };
}

public sealed record StorageOperationResponse(
    Guid OperationId,
    string Action,
    string Status,
    string ResourceId,
    ManagedVolumeResponse? Volume,
    string? ErrorCode,
    bool Retryable,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static StorageOperationResponse From(StorageOperation operation) =>
        new(
            operation.Id,
            StorageOperationPreviewResponse.ActionName(operation.Kind),
            operation.Status.ToString().ToLowerInvariant(),
            operation.ResourceId,
            operation.Volume is null ? null : ManagedVolumeResponse.From(operation.Volume),
            operation.ErrorCode,
            operation.Retryable,
            operation.CreatedAt,
            operation.UpdatedAt);
}
