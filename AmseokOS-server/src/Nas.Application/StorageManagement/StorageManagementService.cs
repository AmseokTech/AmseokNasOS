//--------------------------//
//--------编排数据卷与共享的两阶段确认、Operation 和状态复核---------//
//--------Orchestrates two-phase volume/share operations and reconciliation--------//
//-------------------------//
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nas.Application.Authentication;
using Nas.Application.Storage;
using Nas.Domain.Operations;

namespace Nas.Application.StorageManagement;

public sealed class StorageManagementService(
    IAuthenticationService authentication,
    IStorageInventoryClient storageInventory,
    IStorageManagementClient managedVolumes,
    IStoragePreviewStore previews,
    IStorageOperationStore operations,
    IStorageCommandExecutor executor,
    TimeProvider timeProvider) : IStorageManagementService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions FingerprintJson =
        new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedModes =
        new(StringComparer.Ordinal) { "0750", "0770", "0775", "0777" };

    public Task<IReadOnlyList<ManagedVolumeInformation>> GetVolumesAsync(
        CancellationToken cancellationToken) =>
        managedVolumes.GetManagedVolumesAsync(cancellationToken);

    public async Task<StoragePreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedStorageOperation requested,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(requested);
        if (normalized.Issues.Count > 0)
        {
            return new StoragePreviewRejected(
                StoragePreviewFailure.InvalidRequest,
                normalized.Issues);
        }
        if (!await authentication.VerifyPasswordAsync(userId, password, cancellationToken))
        {
            return new StoragePreviewRejected(
                StoragePreviewFailure.ReauthenticationFailed,
                []);
        }

        var evaluation = await EvaluateAsync(normalized.Request!, cancellationToken);
        var phrase = ConfirmationPhrase(evaluation.Requested, evaluation.Volume);
        if (evaluation.Issues.Count > 0)
        {
            return new StoragePreviewCreated(new StorageOperationPreview(
                evaluation.Requested,
                evaluation.Volume,
                false,
                null,
                null,
                phrase,
                evaluation.Issues,
                evaluation.Warnings));
        }

        var expiresAt = timeProvider.GetUtcNow().Add(PreviewLifetime);
        var ticket = previews.Store(
            userId,
            evaluation.Requested,
            evaluation.ResourceIds,
            evaluation.Fingerprint,
            phrase,
            expiresAt);
        return new StoragePreviewCreated(new StorageOperationPreview(
            evaluation.Requested,
            evaluation.Volume,
            true,
            ticket.Token,
            expiresAt,
            phrase,
            [],
            evaluation.Warnings));
    }

    public async Task<StorageCommandOutcome> ExecuteAsync(
        Guid userId,
        string password,
        string previewToken,
        string confirmationPhrase,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previewToken)
            || previewToken.Length > 256
            || string.IsNullOrWhiteSpace(confirmationPhrase)
            || confirmationPhrase.Length > 256
            || string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > 200)
        {
            return Reject(StorageCommandFailure.InvalidRequest, "storage.request_invalid");
        }
        if (!await authentication.VerifyPasswordAsync(userId, password, cancellationToken))
        {
            return Reject(
                StorageCommandFailure.ReauthenticationFailed,
                "storage.reauthentication_failed");
        }

        var ticket = previews.Consume(userId, previewToken, timeProvider.GetUtcNow());
        if (ticket is null)
        {
            return Reject(StorageCommandFailure.PreviewExpired, "storage.preview_expired");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(ticket.ConfirmationPhrase),
                Encoding.UTF8.GetBytes(confirmationPhrase)))
        {
            return Reject(
                StorageCommandFailure.ConfirmationMismatch,
                "storage.confirmation_mismatch");
        }

        var current = await EvaluateAsync(ticket.Requested, cancellationToken);
        if (current.Issues.Count > 0
            || !string.Equals(
                current.Fingerprint,
                ticket.SnapshotFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                StorageCommandFailure.StateChanged,
                "storage.preview_stale",
                issues: current.Issues);
        }

        var start = await operations.StartAsync(
            userId,
            ticket,
            idempotencyKey,
            cancellationToken);
        if (start is StorageOperationAlreadyExists existing)
        {
            return new StorageCommandSucceeded(existing.Operation);
        }
        if (start is StorageOperationStartRejected rejected)
        {
            return Reject(StorageCommandFailure.Conflict, rejected.Code);
        }
        var operation = ((StorageOperationStarted)start).Operation;

        StorageExecutionOutcome execution;
        try
        {
            execution = await executor.ExecuteAsync(
                new StorageExecutionCommand(
                    operation.Id,
                    operation.IdempotencyKey,
                    operation.FencingToken,
                    ticket.Requested,
                    ticket.SnapshotFingerprint),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await operations.RecordExecutionAsync(
                operation.Id,
                OperationStatus.Interrupted,
                null,
                "storage.execution_cancelled",
                false,
                false,
                CancellationToken.None);
            throw;
        }

        if (execution is StorageExecutionAccepted accepted)
        {
            var completed = await operations.RecordExecutionAsync(
                operation.Id,
                OperationStatus.Succeeded,
                accepted.Volume,
                null,
                false,
                true,
                cancellationToken);
            return new StorageCommandSucceeded(completed);
        }

        var failure = (StorageExecutionRejected)execution;
        var status = failure.OutcomeUncertain
            ? OperationStatus.Interrupted
            : OperationStatus.Failed;
        var failed = await operations.RecordExecutionAsync(
            operation.Id,
            status,
            null,
            failure.Code,
            failure.Retryable,
            !failure.OutcomeUncertain,
            cancellationToken);
        return failure.OutcomeUncertain
            ? new StorageCommandSucceeded(failed)
            : Reject(
                failure.Code is "storage.write_unavailable"
                    or "privileged.unavailable_before_dispatch"
                    ? StorageCommandFailure.ExecutorUnavailable
                    : StorageCommandFailure.ExecutorRejected,
                failure.Code,
                failure.Retryable);
    }

    public async Task<StorageCommandOutcome> GetOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || operationId == Guid.Empty)
        {
            return Reject(
                StorageCommandFailure.OperationNotFound,
                "storage.operation_not_found");
        }
        var operation = await operations.GetAsync(operationId, cancellationToken);
        if (operation is null || operation.UserId != userId)
        {
            return Reject(
                StorageCommandFailure.OperationNotFound,
                "storage.operation_not_found");
        }
        if (operation.Status is not OperationStatus.Interrupted)
        {
            return new StorageCommandSucceeded(operation);
        }

        var volumes = await managedVolumes.GetManagedVolumesAsync(cancellationToken);
        var volume = FindResultVolume(operation.Requested, volumes);
        if (volume is null || !MatchesRequestedState(operation.Requested, volume))
        {
            return new StorageCommandSucceeded(operation);
        }
        var reconciled = await operations.RecordExecutionAsync(
            operation.Id,
            OperationStatus.Succeeded,
            volume,
            null,
            false,
            true,
            cancellationToken);
        return new StorageCommandSucceeded(reconciled);
    }

    private async Task<Evaluation> EvaluateAsync(
        RequestedStorageOperation requested,
        CancellationToken cancellationToken)
    {
        var arraysTask = storageInventory.GetRaidArraysAsync(cancellationToken);
        var volumesTask = managedVolumes.GetManagedVolumesAsync(cancellationToken);
        await Task.WhenAll(arraysTask, volumesTask);
        var arrays = await arraysTask;
        var volumes = await volumesTask;
        var issues = new List<StorageOperationIssue>();
        var warnings = new List<string>();
        RaidArrayInformation? array = null;
        ManagedVolumeInformation? volume = null;

        if (requested.Kind == StorageOperationKind.ProvisionVolume)
        {
            array = arrays.SingleOrDefault(item => item.Id == requested.ArrayId);
            if (array is null)
            {
                issues.Add(Issue("storage.array_not_found", "arrayId", "目标 RAID 阵列不存在"));
            }
            else
            {
                if (array.Uuid is null || !array.Id.StartsWith("md:", StringComparison.Ordinal))
                {
                    issues.Add(Issue(
                        "storage.array_identity_unsafe",
                        "arrayId",
                        "阵列缺少稳定 MD UUID"));
                }
                if (array.DegradedDeviceCount != 0
                    || !string.Equals(array.SyncAction, "idle", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Issue(
                        "storage.array_not_ready",
                        "arrayId",
                        "阵列必须无降级且同步空闲"));
                }
                if (volumes.Any(item => item.ArrayId == array.Id))
                {
                    issues.Add(Issue(
                        "storage.array_already_managed",
                        "arrayId",
                        "阵列已经属于一个受管数据卷"));
                }
            }
            if (volumes.Any(item => item.Name == requested.VolumeName))
            {
                issues.Add(Issue(
                    "storage.volume_name_conflict",
                    "volumeName",
                    "数据卷名称已经存在"));
            }
            warnings.Add("storage.array_data_will_be_destroyed");
            warnings.Add("storage.ext4_only");
        }
        else
        {
            volume = volumes.SingleOrDefault(item => item.Id == requested.VolumeId);
            if (volume is null)
            {
                issues.Add(Issue(
                    "storage.volume_not_found",
                    "volumeId",
                    "目标受管数据卷不存在"));
            }
            else if (!volume.Mounted || !volume.PersistentMountEnabled)
            {
                issues.Add(Issue(
                    "storage.volume_not_ready",
                    "volumeId",
                    "数据卷必须已按 UUID 持久挂载"));
            }
        }

        if (requested.Kind is StorageOperationKind.ProvisionVolume
            or StorageOperationKind.UpdatePermissions)
        {
            ValidatePermissions(requested, issues);
        }
        if (requested.Kind is StorageOperationKind.ProvisionVolume
            or StorageOperationKind.ConfigureShares)
        {
            ValidateShares(
                requested,
                requested.DirectoryMode ?? volume?.DirectoryMode,
                issues,
                warnings);
        }

        var resources = new[]
        {
            requested.ArrayId is null ? null : $"array:{requested.ArrayId}",
            requested.VolumeId is null ? null : $"volume:{requested.VolumeId}",
            requested.VolumeName is null ? null : $"volume-name:{requested.VolumeName}",
            requested.Smb?.ShareName is null ? null : $"smb-share:{requested.Smb.ShareName}"
        }.Where(value => value is not null).Cast<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var fingerprint = Fingerprint(requested, array, volume, volumes);
        return new Evaluation(requested, volume, resources, fingerprint, issues, warnings);
    }

    private static Normalization Normalize(RequestedStorageOperation requested)
    {
        var issues = new List<StorageOperationIssue>();
        var normalized = requested with
        {
            ArrayId = NullIfEmpty(requested.ArrayId),
            VolumeId = NullIfEmpty(requested.VolumeId),
            VolumeName = NullIfEmpty(requested.VolumeName)?.ToLowerInvariant(),
            OwnerName = NullIfEmpty(requested.OwnerName),
            GroupName = NullIfEmpty(requested.GroupName),
            DirectoryMode = NullIfEmpty(requested.DirectoryMode),
            Smb = requested.Smb is null ? null : requested.Smb with
            {
                ShareName = NullIfEmpty(requested.Smb.ShareName)?.ToLowerInvariant(),
                AllowedNetwork = NullIfEmpty(requested.Smb.AllowedNetwork)
            },
            Nfs = requested.Nfs is null ? null : requested.Nfs with
            {
                ClientNetwork = NullIfEmpty(requested.Nfs.ClientNetwork)
            }
        };

        if (normalized.Kind == StorageOperationKind.ProvisionVolume)
        {
            if (normalized.ArrayId is null)
            {
                issues.Add(Issue("storage.array_id_required", "arrayId", "必须选择 RAID 阵列"));
            }
            if (!ValidName(normalized.VolumeName))
            {
                issues.Add(Issue(
                    "storage.volume_name_invalid",
                    "volumeName",
                    "卷名必须以字母开头，只能包含小写字母、数字和连字符"));
            }
        }
        else if (normalized.VolumeId is null)
        {
            issues.Add(Issue("storage.volume_id_required", "volumeId", "必须选择受管数据卷"));
        }
        return new Normalization(normalized, issues);
    }

    private static void ValidatePermissions(
        RequestedStorageOperation requested,
        ICollection<StorageOperationIssue> issues)
    {
        if (!ValidAccountName(requested.OwnerName))
        {
            issues.Add(Issue("storage.owner_invalid", "ownerName", "目录所有者名称无效"));
        }
        if (!ValidAccountName(requested.GroupName))
        {
            issues.Add(Issue("storage.group_invalid", "groupName", "目录组名称无效"));
        }
        if (requested.DirectoryMode is null || !AllowedModes.Contains(requested.DirectoryMode))
        {
            issues.Add(Issue(
                "storage.mode_invalid",
                "directoryMode",
                "目录权限只允许 0750、0770、0775 或 0777"));
        }
    }

    private static void ValidateShares(
        RequestedStorageOperation requested,
        string? effectiveDirectoryMode,
        ICollection<StorageOperationIssue> issues,
        ICollection<string> warnings)
    {
        var smb = requested.Smb ?? new SmbShareSettings(false, null, true, false, null);
        var nfs = requested.Nfs ?? new NfsShareSettings(false, null, true);
        if (smb.Enabled)
        {
            if (!ValidName(smb.ShareName))
            {
                issues.Add(Issue("storage.smb_name_invalid", "smb.shareName", "SMB 共享名无效"));
            }
            if (!ValidIpv4Network(smb.AllowedNetwork))
            {
                issues.Add(Issue(
                    "storage.smb_network_invalid",
                    "smb.allowedNetwork",
                    "SMB 必须限制到有效 IPv4 CIDR"));
            }
            if (smb.GuestAccess)
            {
                warnings.Add("storage.smb_guest_access_enabled");
                if (!smb.ReadOnly && effectiveDirectoryMode != "0777")
                {
                    issues.Add(Issue(
                        "storage.smb_guest_write_requires_0777",
                        "directoryMode",
                        "匿名 SMB 写入要求目录权限为 0777"));
                }
            }
        }
        if (nfs.Enabled)
        {
            if (!ValidIpv4Network(nfs.ClientNetwork))
            {
                issues.Add(Issue(
                    "storage.nfs_network_invalid",
                    "nfs.clientNetwork",
                    "NFS 必须限制到有效 IPv4 CIDR"));
            }
            warnings.Add("storage.nfs_root_squash_enabled");
        }
    }

    private static bool MatchesRequestedState(
        RequestedStorageOperation requested,
        ManagedVolumeInformation volume) => requested.Kind switch
        {
            StorageOperationKind.ProvisionVolume =>
                volume.Name == requested.VolumeName
                && volume.Mounted && volume.PersistentMountEnabled
                && volume.ReadWriteVerified,
            StorageOperationKind.UpdatePermissions =>
                volume.OwnerName == requested.OwnerName
                && volume.GroupName == requested.GroupName
                && volume.DirectoryMode == requested.DirectoryMode,
            StorageOperationKind.ConfigureShares =>
                volume.Smb == requested.Smb && volume.Nfs == requested.Nfs,
            StorageOperationKind.VerifyReadWrite => volume.ReadWriteVerified,
            _ => false
        };

    private static ManagedVolumeInformation? FindResultVolume(
        RequestedStorageOperation requested,
        IReadOnlyList<ManagedVolumeInformation> volumes) =>
        requested.Kind == StorageOperationKind.ProvisionVolume
            ? volumes.SingleOrDefault(item => item.Name == requested.VolumeName)
            : volumes.SingleOrDefault(item => item.Id == requested.VolumeId);

    private static string Fingerprint(
        RequestedStorageOperation requested,
        RaidArrayInformation? array,
        ManagedVolumeInformation? volume,
        IReadOnlyList<ManagedVolumeInformation> volumes)
    {
        var snapshot = new
        {
            requested,
            Array = array is null ? null : new
            {
                array.Id,
                array.Uuid,
                array.Path,
                array.State,
                array.DegradedDeviceCount,
                array.SyncAction,
                Members = array.Members.Select(member => new
                {
                    member.Path,
                    member.State,
                    member.Slot
                }).OrderBy(member => member.Path)
            },
            Volume = volume,
            ExistingNames = volumes.Select(item => item.Name).Order()
        };
        return Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, FingerprintJson)));
    }

    private static string ConfirmationPhrase(
        RequestedStorageOperation requested,
        ManagedVolumeInformation? volume) => requested.Kind switch
        {
            StorageOperationKind.ProvisionVolume => $"格式化并挂载 {requested.VolumeName}",
            StorageOperationKind.UpdatePermissions => $"更新权限 {volume?.Name ?? requested.VolumeId}",
            StorageOperationKind.ConfigureShares => $"更新共享 {volume?.Name ?? requested.VolumeId}",
            StorageOperationKind.VerifyReadWrite => $"校验读写 {volume?.Name ?? requested.VolumeId}",
            _ => "确认存储操作"
        };

    private static bool ValidName(string? value) =>
        value is { Length: >= 1 and <= 32 }
        && char.IsAsciiLetter(value[0])
        && value.All(character => character is >= 'a' and <= 'z'
            || char.IsAsciiDigit(character) || character == '-');

    private static bool ValidAccountName(string? value) =>
        value is { Length: >= 1 and <= 32 }
        && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-');

    private static bool ValidIpv4Network(string? value)
    {
        if (value is null || value.Length > 43)
        {
            return false;
        }
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && IPAddress.TryParse(parts[0], out var address)
            && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && int.TryParse(parts[1], out var prefix)
            && prefix is >= 1 and <= 32;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StorageOperationIssue Issue(string code, string field, string message) =>
        new(code, field, message);

    private static StorageCommandRejected Reject(
        StorageCommandFailure failure,
        string code,
        bool retryable = false,
        IReadOnlyList<StorageOperationIssue>? issues = null) =>
        new(failure, code, retryable, issues ?? []);

    private sealed record Normalization(
        RequestedStorageOperation? Request,
        IReadOnlyList<StorageOperationIssue> Issues);

    private sealed record Evaluation(
        RequestedStorageOperation Requested,
        ManagedVolumeInformation? Volume,
        IReadOnlyList<string> ResourceIds,
        string Fingerprint,
        IReadOnlyList<StorageOperationIssue> Issues,
        IReadOnlyList<string> Warnings);
}
