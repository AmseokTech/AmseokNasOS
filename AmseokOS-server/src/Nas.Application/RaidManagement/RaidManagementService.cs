//--------------------------//
//--------编排 RAID 预检、二次认证、幂等操作与执行复核---------//
//--------Orchestrates RAID previews, reauthentication, idempotent operations, and execution checks--------//
//-------------------------//
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nas.Application.Authentication;
using Nas.Application.Storage;
using Nas.Domain.Operations;

namespace Nas.Application.RaidManagement;

public sealed class RaidManagementService(
    IAuthenticationService authentication,
    IStorageInventoryClient inventory,
    IRaidPreviewStore previews,
    IRaidOperationStore operations,
    IRaidCommandExecutor executor,
    TimeProvider timeProvider) : IRaidManagementService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions FingerprintJson = new(JsonSerializerDefaults.Web);

    public async Task<RaidPreviewOutcome> CreatePreviewAsync(
        Guid userId,
        string password,
        RequestedRaidOperation requested,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(requested);
        if (normalized.Issues.Count > 0)
        {
            return new RaidPreviewRejected(RaidPreviewFailure.InvalidRequest, normalized.Issues);
        }

        if (!await authentication.VerifyPasswordAsync(userId, password, cancellationToken))
        {
            return new RaidPreviewRejected(RaidPreviewFailure.ReauthenticationFailed, []);
        }

        var evaluation = await EvaluateAsync(normalized.Request!, cancellationToken);
        var phrase = ConfirmationPhrase(evaluation.Requested, evaluation.Array?.Name);
        if (evaluation.Issues.Count > 0)
        {
            return new RaidPreviewCreated(new RaidOperationPreview(
                evaluation.Requested,
                evaluation.Array?.Name,
                evaluation.MemberDeviceIds,
                CanExecute: false,
                PreviewToken: null,
                ExpiresAt: null,
                phrase,
                evaluation.Issues,
                evaluation.Warnings));
        }

        var expiresAt = timeProvider.GetUtcNow().Add(PreviewLifetime);
        var ticket = previews.Store(
            userId,
            evaluation.Requested,
            evaluation.Array?.Name,
            evaluation.MemberDeviceIds,
            evaluation.ResourceIds,
            evaluation.Fingerprint,
            phrase,
            expiresAt);
        return new RaidPreviewCreated(new RaidOperationPreview(
            evaluation.Requested,
            evaluation.Array?.Name,
            evaluation.MemberDeviceIds,
            CanExecute: true,
            ticket.Token,
            expiresAt,
            phrase,
            [],
            evaluation.Warnings));
    }

    public async Task<RaidCommandOutcome> ExecuteAsync(
        Guid userId,
        string password,
        string previewToken,
        string confirmationPhrase,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var inputIssues = ValidateExecutionInput(
            previewToken,
            confirmationPhrase,
            idempotencyKey);
        if (inputIssues.Count > 0)
        {
            return Reject(RaidCommandFailure.InvalidRequest, "raid.request_invalid", inputIssues);
        }

        if (!await authentication.VerifyPasswordAsync(userId, password, cancellationToken))
        {
            return Reject(
                RaidCommandFailure.ReauthenticationFailed,
                "raid.reauthentication_failed");
        }

        var ticket = previews.Consume(userId, previewToken, timeProvider.GetUtcNow());
        if (ticket is null)
        {
            return Reject(RaidCommandFailure.PreviewExpired, "raid.preview_expired");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(ticket.ConfirmationPhrase),
                Encoding.UTF8.GetBytes(confirmationPhrase)))
        {
            return Reject(
                RaidCommandFailure.ConfirmationMismatch,
                "raid.confirmation_mismatch");
        }

        var current = await EvaluateAsync(ticket.Requested, cancellationToken);
        if (current.Issues.Count > 0
            || !string.Equals(
                current.Fingerprint,
                ticket.SnapshotFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                RaidCommandFailure.StateChanged,
                "raid.preview_stale",
                current.Issues);
        }

        var start = await operations.StartAsync(
            userId,
            ticket,
            idempotencyKey,
            cancellationToken);
        if (start is RaidOperationAlreadyExists existing)
        {
            return new RaidCommandSucceeded(existing.Operation);
        }
        if (start is RaidOperationStartRejected rejected)
        {
            return Reject(RaidCommandFailure.Conflict, rejected.Code);
        }
        var operation = ((RaidOperationStarted)start).Operation;

        RaidExecutionOutcome execution;
        try
        {
            execution = await executor.ExecuteAsync(
                new RaidExecutionCommand(
                    operation.Id,
                    operation.IdempotencyKey,
                    operation.FencingToken,
                    ticket.Requested,
                    ticket.ExpectedMemberDeviceIds,
                    ticket.SnapshotFingerprint),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await operations.RecordExecutionAsync(
                operation.Id,
                OperationStatus.Interrupted,
                null,
                "raid.execution_cancelled",
                retryable: false,
                null,
                releaseLocks: false,
                CancellationToken.None);
            throw;
        }

        if (execution is RaidExecutionAccepted accepted)
        {
            var status = accepted.InProgress
                ? OperationStatus.Running
                : OperationStatus.Succeeded;
            var updated = await operations.RecordExecutionAsync(
                operation.Id,
                status,
                accepted.ArrayId,
                null,
                retryable: false,
                accepted.ProgressPercentage,
                releaseLocks: status == OperationStatus.Succeeded,
                cancellationToken);
            return new RaidCommandSucceeded(updated);
        }

        var failure = (RaidExecutionRejected)execution;
        var failureStatus = failure.OutcomeUncertain
            ? OperationStatus.Interrupted
            : OperationStatus.Failed;
        var failed = await operations.RecordExecutionAsync(
            operation.Id,
            failureStatus,
            null,
            failure.Code,
            failure.Retryable,
            null,
            releaseLocks: !failure.OutcomeUncertain,
            cancellationToken);
        return failure.OutcomeUncertain
            ? new RaidCommandSucceeded(failed)
            : Reject(
                failure.Code is "raid.write_unavailable" or "privileged.unavailable_before_dispatch"
                    ? RaidCommandFailure.ExecutorUnavailable
                    : RaidCommandFailure.ExecutorRejected,
                failure.Code,
                retryable: failure.Retryable);
    }

    public async Task<RaidCommandOutcome> GetOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || operationId == Guid.Empty)
        {
            return Reject(RaidCommandFailure.OperationNotFound, "raid.operation_not_found");
        }

        var operation = await operations.GetAsync(operationId, cancellationToken);
        if (operation is null || operation.UserId != userId)
        {
            return Reject(RaidCommandFailure.OperationNotFound, "raid.operation_not_found");
        }
        if (operation.Status is not (OperationStatus.Running or OperationStatus.Interrupted))
        {
            return new RaidCommandSucceeded(operation);
        }

        var disksTask = inventory.GetBlockDevicesAsync(cancellationToken);
        var arraysTask = inventory.GetRaidArraysAsync(cancellationToken);
        await Task.WhenAll(disksTask, arraysTask);
        var reconciliation = ReconcileOperation(
            operation,
            await disksTask,
            await arraysTask);
        if (reconciliation.State == ReconciliationState.Unknown)
        {
            return new RaidCommandSucceeded(operation);
        }
        if (reconciliation.State == ReconciliationState.InProgress)
        {
            var updated = await operations.RecordExecutionAsync(
                operation.Id,
                OperationStatus.Running,
                reconciliation.ArrayId,
                null,
                retryable: false,
                reconciliation.ProgressPercentage,
                releaseLocks: false,
                cancellationToken);
            return new RaidCommandSucceeded(updated);
        }

        var finalStatus = reconciliation.State == ReconciliationState.Succeeded
            ? OperationStatus.Succeeded
            : OperationStatus.Failed;
        var completed = await operations.RecordExecutionAsync(
            operation.Id,
            finalStatus,
            reconciliation.ArrayId,
            finalStatus == OperationStatus.Failed ? "raid.result_verification_failed" : null,
            retryable: false,
            finalStatus == OperationStatus.Succeeded ? 100 : reconciliation.ProgressPercentage,
            releaseLocks: true,
            cancellationToken);
        return new RaidCommandSucceeded(completed);
    }

    private static Reconciliation ReconcileOperation(
        RaidOperation operation,
        IReadOnlyList<BlockDeviceInformation> disks,
        IReadOnlyList<RaidArrayInformation> arrays)
    {
        var array = operation.ArrayId is null
            ? null
            : arrays.SingleOrDefault(item => item.Id == operation.ArrayId);
        if (array is null && operation.Kind == RaidOperationKind.Create)
        {
            var requestedMembers = operation.Requested.DeviceIds.ToHashSet(StringComparer.Ordinal);
            var candidates = arrays.Where(candidate =>
            {
                var issues = new List<RaidOperationIssue>();
                var members = ResolveMemberDeviceIds(candidate, disks, issues);
                return issues.Count == 0
                    && members.ToHashSet(StringComparer.Ordinal).SetEquals(requestedMembers);
            }).Take(2).ToArray();
            array = candidates.Length == 1 ? candidates[0] : null;
        }
        if (operation.Kind == RaidOperationKind.Delete)
        {
            return array is null
                ? new Reconciliation(ReconciliationState.Succeeded, null, 100)
                : new Reconciliation(
                    string.Equals(array.SyncAction, "idle", StringComparison.OrdinalIgnoreCase)
                        ? ReconciliationState.Failed
                        : ReconciliationState.InProgress,
                    array.Id,
                    SyncPercentage(array));
        }
        if (array is null)
        {
            return new Reconciliation(ReconciliationState.Unknown, operation.ArrayId, null);
        }
        if (!string.Equals(array.SyncAction, "idle", StringComparison.OrdinalIgnoreCase))
        {
            return new Reconciliation(
                ReconciliationState.InProgress,
                array.Id,
                SyncPercentage(array));
        }

        var memberIssues = new List<RaidOperationIssue>();
        var members = ResolveMemberDeviceIds(array, disks, memberIssues);
        if (memberIssues.Count > 0)
        {
            return new Reconciliation(ReconciliationState.Unknown, array.Id, null);
        }
        var memberSet = members.ToHashSet(StringComparer.Ordinal);
        var requested = operation.Requested;
        var succeeded = operation.Kind switch
        {
            RaidOperationKind.Create =>
                memberSet.SetEquals(requested.DeviceIds) && array.DegradedDeviceCount == 0,
            RaidOperationKind.AddDevice =>
                requested.DeviceIds.All(memberSet.Contains),
            RaidOperationKind.RemoveDevice =>
                requested.SourceDeviceId is not null
                    && !memberSet.Contains(requested.SourceDeviceId),
            RaidOperationKind.ReplaceDevice =>
                requested.SourceDeviceId is not null
                    && !memberSet.Contains(requested.SourceDeviceId)
                    && requested.DeviceIds.All(memberSet.Contains)
                    && array.DegradedDeviceCount == 0,
            RaidOperationKind.Grow or RaidOperationKind.Shrink =>
                requested.TargetDeviceCount == array.ConfiguredDeviceCount
                    && array.DegradedDeviceCount == 0,
            _ => false
        };
        return new Reconciliation(
            succeeded ? ReconciliationState.Succeeded : ReconciliationState.Failed,
            array.Id,
            succeeded ? 100 : SyncPercentage(array));
    }

    private async Task<Evaluation> EvaluateAsync(
        RequestedRaidOperation requested,
        CancellationToken cancellationToken)
    {
        var disksTask = inventory.GetBlockDevicesAsync(cancellationToken);
        var arraysTask = inventory.GetRaidArraysAsync(cancellationToken);
        await Task.WhenAll(disksTask, arraysTask);
        var disks = await disksTask;
        var arrays = await arraysTask;
        var issues = new List<RaidOperationIssue>();
        var warnings = new List<string>();
        var targetArray = FindArray(requested.ArrayId, arrays, issues);
        var memberDeviceIds = targetArray is null
            ? []
            : ResolveMemberDeviceIds(targetArray, disks, issues);
        if (requested.Kind != RaidOperationKind.Create
            && disks.Any(disk => memberDeviceIds.Contains(disk.Id) && disk.SystemDevice))
        {
            issues.Add(Issue(
                "raid.array_contains_system_disk",
                "arrayId",
                "包含系统盘的阵列禁止执行写操作"));
        }

        switch (requested.Kind)
        {
            case RaidOperationKind.Create:
                ValidateCreate(requested, disks, issues, warnings);
                break;
            case RaidOperationKind.Delete:
                ValidateExistingArray(targetArray, requireIdle: true, issues);
                warnings.Add("raid.all_array_data_will_be_destroyed");
                break;
            case RaidOperationKind.AddDevice:
                ValidateExistingArray(targetArray, requireIdle: true, issues);
                ValidateLevelSupportsMemberManagement(targetArray, issues);
                if (targetArray?.ConfiguredDeviceCount >= 64)
                {
                    issues.Add(Issue(
                        "raid.maximum_member_count_reached",
                        "arrayId",
                        "阵列成员数量已达到 64 块上限"));
                }
                ValidateNewDevices(requested.DeviceIds, disks, expectedCount: 1, issues);
                warnings.Add("raid.selected_disks_will_be_erased");
                break;
            case RaidOperationKind.RemoveDevice:
                ValidateExistingArray(targetArray, requireIdle: true, issues);
                ValidateLevelSupportsMemberManagement(targetArray, issues);
                ValidateSourceMember(requested.SourceDeviceId, memberDeviceIds, issues);
                warnings.Add("raid.array_may_become_degraded");
                break;
            case RaidOperationKind.ReplaceDevice:
                ValidateExistingArray(targetArray, requireIdle: true, issues);
                ValidateLevelSupportsMemberManagement(targetArray, issues);
                ValidateSourceMember(requested.SourceDeviceId, memberDeviceIds, issues);
                ValidateNewDevices(requested.DeviceIds, disks, expectedCount: 1, issues);
                warnings.Add("raid.selected_disks_will_be_erased");
                warnings.Add("raid.array_may_become_degraded");
                break;
            case RaidOperationKind.Grow:
                ValidateResize(requested, targetArray, disks, grow: true, issues, warnings);
                break;
            case RaidOperationKind.Shrink:
                ValidateResize(requested, targetArray, disks, grow: false, issues, warnings);
                break;
            default:
                issues.Add(Issue("raid.action_invalid", "action", "RAID 操作类型不受支持"));
                break;
        }

        var resourceIds = requested.DeviceIds
            .Append(requested.SourceDeviceId)
            .Concat(memberDeviceIds)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => $"disk:{value}")
            .Append(targetArray is null
                ? $"array-name:{requested.ArrayName}"
                : $"array:{targetArray.Id}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fingerprint = Fingerprint(requested, targetArray, disks, memberDeviceIds);
        return new Evaluation(
            requested,
            targetArray,
            memberDeviceIds,
            resourceIds,
            fingerprint,
            issues,
            warnings);
    }

    private static Normalization Normalize(RequestedRaidOperation requested)
    {
        var issues = new List<RaidOperationIssue>();
        var deviceIds = (requested.DeviceIds ?? [])
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (deviceIds.Length > 64)
        {
            issues.Add(Issue("raid.device_count_too_large", "deviceIds", "单次操作最多选择 64 块磁盘"));
        }
        if (deviceIds.Any(value => value.Length > 295))
        {
            issues.Add(Issue("raid.device_id_invalid", "deviceIds", "磁盘标识过长"));
        }
        var arrayId = NullIfEmpty(requested.ArrayId);
        var sourceDeviceId = NullIfEmpty(requested.SourceDeviceId);
        if (arrayId?.Length > 294)
        {
            issues.Add(Issue("raid.array_id_invalid", "arrayId", "阵列标识过长"));
        }
        if (sourceDeviceId?.Length > 295)
        {
            issues.Add(Issue("raid.source_device_id_invalid", "sourceDeviceId", "成员磁盘标识过长"));
        }
        var arrayName = NullIfEmpty(requested.ArrayName)?.ToLowerInvariant();
        var level = NormalizeLevel(requested.Level);
        if (requested.Kind == RaidOperationKind.Create)
        {
            if (arrayName is null
                || arrayName.Length > 32
                || !char.IsAsciiLetter(arrayName[0])
                || arrayName.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                issues.Add(Issue(
                    "raid.array_name_invalid",
                    "arrayName",
                    "阵列名称必须以字母开头，且只能包含字母、数字和连字符"));
            }
            if (level is null)
            {
                issues.Add(Issue("raid.level_invalid", "level", "请选择受支持的 RAID 级别"));
            }
        }
        else if (arrayId is null)
        {
            issues.Add(Issue("raid.array_id_required", "arrayId", "必须选择目标 RAID 阵列"));
        }

        return new Normalization(
            new RequestedRaidOperation(
                requested.Kind,
                arrayId,
                arrayName,
                level,
                deviceIds,
                sourceDeviceId,
                requested.TargetDeviceCount),
            issues);
    }

    private static void ValidateCreate(
        RequestedRaidOperation requested,
        IReadOnlyList<BlockDeviceInformation> disks,
        ICollection<RaidOperationIssue> issues,
        ICollection<string> warnings)
    {
        var minimum = MinimumDevices(requested.Level);
        if (minimum is null || requested.DeviceIds.Count < minimum)
        {
            issues.Add(Issue(
                "raid.device_count_invalid",
                "deviceIds",
                $"{requested.Level?.ToUpperInvariant() ?? "所选级别"} 至少需要 {minimum ?? 2} 块磁盘"));
        }
        if (requested.Level == "raid10" && requested.DeviceIds.Count % 2 != 0)
        {
            issues.Add(Issue(
                "raid10.device_count_must_be_even",
                "deviceIds",
                "RAID10 必须选择偶数块磁盘"));
        }
        ValidateNewDevices(requested.DeviceIds, disks, null, issues);
        warnings.Add("raid.selected_disks_will_be_erased");
    }

    private static void ValidateResize(
        RequestedRaidOperation requested,
        RaidArrayInformation? array,
        IReadOnlyList<BlockDeviceInformation> disks,
        bool grow,
        ICollection<RaidOperationIssue> issues,
        ICollection<string> warnings)
    {
        ValidateExistingArray(array, requireIdle: true, issues);
        if (array is null)
        {
            return;
        }
        var level = NormalizeLevel(array.Level);
        if (level is not ("raid0" or "raid1" or "raid5" or "raid6"))
        {
            issues.Add(Issue(
                "raid.reshape_level_unsupported",
                "arrayId",
                "当前 RAID 级别不支持安全调整成员数量"));
        }
        if (array.ConfiguredDeviceCount is < 1 or > 64)
        {
            issues.Add(Issue(
                "raid.current_device_count_invalid",
                "arrayId",
                "当前阵列成员数量超出支持范围"));
            return;
        }
        var target = requested.TargetDeviceCount;
        var current = (int)array.ConfiguredDeviceCount;
        if (target is null || (grow ? target <= current : target >= current))
        {
            issues.Add(Issue(
                "raid.target_device_count_invalid",
                "targetDeviceCount",
                grow ? "扩容后的成员数必须大于当前成员数" : "缩容后的成员数必须小于当前成员数"));
            return;
        }
        if (target > 64)
        {
            issues.Add(Issue(
                "raid.target_device_count_too_large",
                "targetDeviceCount",
                "阵列成员数量不能超过 64 块"));
        }
        var minimum = MinimumDevices(level) ?? int.MaxValue;
        if (target < minimum || level == "raid10" && target % 2 != 0)
        {
            issues.Add(Issue(
                "raid.target_device_count_below_minimum",
                "targetDeviceCount",
                "目标成员数低于该 RAID 级别的安全下限"));
        }
        if (grow)
        {
            var expected = target.Value - current;
            ValidateNewDevices(requested.DeviceIds, disks, expected, issues);
            warnings.Add("raid.selected_disks_will_be_erased");
            warnings.Add("raid.reshape_may_take_a_long_time");
        }
        else
        {
            if (requested.DeviceIds.Count > 0)
            {
                issues.Add(Issue(
                    "raid.shrink_devices_forbidden",
                    "deviceIds",
                    "缩容由 mdadm 在重塑后产生备用盘，不能预先指定要移除的活动成员"));
            }
            warnings.Add("raid.shrink_requires_raw_unmounted_array");
            warnings.Add("raid.reshape_backup_required");
        }
    }

    private static void ValidateExistingArray(
        RaidArrayInformation? array,
        bool requireIdle,
        ICollection<RaidOperationIssue> issues)
    {
        if (array is null)
        {
            return;
        }
        if (array.Uuid is null || !array.Id.StartsWith("md:", StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "raid.array_identity_unstable",
                "arrayId",
                "阵列缺少稳定 UUID，禁止执行写操作"));
        }
        if (requireIdle && !string.Equals(array.SyncAction, "idle", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "raid.array_busy",
                "arrayId",
                "阵列正在同步、恢复或重塑，不能开始新的操作"));
        }
    }

    private static void ValidateLevelSupportsMemberManagement(
        RaidArrayInformation? array,
        ICollection<RaidOperationIssue> issues)
    {
        if (array is not null && NormalizeLevel(array.Level) == "raid0")
        {
            issues.Add(Issue(
                "raid0.member_management_unsupported",
                "arrayId",
                "RAID0 不支持热备、故障替换或单独移除成员"));
        }
    }

    private static void ValidateSourceMember(
        string? sourceDeviceId,
        IReadOnlyList<string> memberDeviceIds,
        ICollection<RaidOperationIssue> issues)
    {
        if (sourceDeviceId is null || !memberDeviceIds.Contains(sourceDeviceId, StringComparer.Ordinal))
        {
            issues.Add(Issue(
                "raid.source_member_not_found",
                "sourceDeviceId",
                "必须选择当前阵列中的成员磁盘"));
        }
    }

    private static void ValidateNewDevices(
        IReadOnlyList<string> deviceIds,
        IReadOnlyList<BlockDeviceInformation> disks,
        int? expectedCount,
        ICollection<RaidOperationIssue> issues)
    {
        if (expectedCount is not null && deviceIds.Count != expectedCount)
        {
            issues.Add(Issue(
                "raid.device_count_mismatch",
                "deviceIds",
                $"该操作必须选择 {expectedCount} 块新磁盘"));
        }
        foreach (var deviceId in deviceIds)
        {
            var matches = disks.Where(item => item.Id == deviceId).Take(2).ToArray();
            if (matches.Length != 1)
            {
                issues.Add(Issue("raid.device_not_found", "deviceIds", $"磁盘 {deviceId} 不存在或身份冲突"));
                continue;
            }
            var disk = matches[0];
            if (!disk.Stable || disk.IdentityConflict || !disk.TopologyComplete)
            {
                issues.Add(Issue("raid.device_identity_unsafe", "deviceIds", $"磁盘 {deviceId} 的身份或拓扑不可靠"));
            }
            if (disk.SystemDevice || disk.Swap || disk.InUse || disk.RaidMember
                || disk.ReadOnly || disk.Removable || disk.Partitions.Count > 0
                || disk.MountPoints.Count > 0 || disk.DependentDevices.Count > 0)
            {
                issues.Add(Issue("raid.device_busy", "deviceIds", $"磁盘 {deviceId} 是系统盘、已占用或不可写"));
            }
        }
    }

    private static RaidArrayInformation? FindArray(
        string? arrayId,
        IReadOnlyList<RaidArrayInformation> arrays,
        ICollection<RaidOperationIssue> issues)
    {
        if (arrayId is null)
        {
            return null;
        }
        var matches = arrays.Where(item => item.Id == arrayId).Take(2).ToArray();
        if (matches.Length != 1)
        {
            issues.Add(Issue("raid.array_not_found", "arrayId", "目标阵列已不存在或身份发生变化"));
            return null;
        }
        return matches[0];
    }

    private static IReadOnlyList<string> ResolveMemberDeviceIds(
        RaidArrayInformation array,
        IReadOnlyList<BlockDeviceInformation> disks,
        ICollection<RaidOperationIssue> issues)
    {
        var result = new List<string>();
        foreach (var member in array.Members)
        {
            var matches = disks.Where(disk =>
                    disk.Path == member.Path
                    || disk.Partitions.Any(partition => partition.Path == member.Path))
                .Take(2)
                .ToArray();
            if (matches.Length != 1 || !matches[0].Stable || matches[0].IdentityConflict)
            {
                issues.Add(Issue(
                    "raid.member_identity_unresolved",
                    "arrayId",
                    $"无法把阵列成员 {member.Name} 唯一映射到稳定物理磁盘"));
                continue;
            }
            result.Add(matches[0].Id);
        }
        return result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string Fingerprint(
        RequestedRaidOperation requested,
        RaidArrayInformation? array,
        IReadOnlyList<BlockDeviceInformation> disks,
        IReadOnlyList<string> memberDeviceIds)
    {
        var selectedIds = requested.DeviceIds
            .Append(requested.SourceDeviceId)
            .Concat(memberDeviceIds)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        var snapshot = new
        {
            requested,
            Array = array is null ? null : new
            {
                array.Id,
                array.Uuid,
                array.Level,
                array.State,
                array.ConfiguredDeviceCount,
                array.DegradedDeviceCount,
                array.SyncAction,
                Members = array.Members.Select(member => new
                {
                    member.Path,
                    member.State,
                    member.Slot
                }).OrderBy(member => member.Path)
            },
            Disks = disks.Where(disk => selectedIds.Contains(disk.Id)).Select(disk => new
            {
                disk.Id,
                disk.SizeBytes,
                disk.Stable,
                disk.IdentityConflict,
                disk.TopologyComplete,
                disk.SystemDevice,
                disk.Swap,
                disk.RaidMember,
                disk.InUse,
                disk.ReadOnly,
                Partitions = disk.Partitions.Select(partition => partition.Path).Order()
            }).OrderBy(disk => disk.Id)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, FingerprintJson);
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string ConfirmationPhrase(RequestedRaidOperation requested, string? arrayName)
    {
        return requested.Kind switch
        {
            RaidOperationKind.Create => $"创建 {requested.ArrayName}",
            RaidOperationKind.Delete => $"删除 {arrayName}",
            RaidOperationKind.AddDevice => $"添加磁盘到 {arrayName}",
            RaidOperationKind.RemoveDevice => $"移除 {requested.SourceDeviceId} 从 {arrayName}",
            RaidOperationKind.ReplaceDevice => $"替换 {arrayName} 的磁盘",
            RaidOperationKind.Grow => $"扩容 {arrayName} 到 {requested.TargetDeviceCount} 块",
            RaidOperationKind.Shrink => $"缩容 {arrayName} 到 {requested.TargetDeviceCount} 块",
            _ => "确认 RAID 操作"
        };
    }

    private static IReadOnlyList<RaidOperationIssue> ValidateExecutionInput(
        string previewToken,
        string confirmationPhrase,
        string idempotencyKey)
    {
        var issues = new List<RaidOperationIssue>();
        if (string.IsNullOrWhiteSpace(previewToken) || previewToken.Length > 256)
        {
            issues.Add(Issue("raid.preview_token_invalid", "previewToken", "预览令牌无效"));
        }
        if (string.IsNullOrWhiteSpace(confirmationPhrase) || confirmationPhrase.Length > 256)
        {
            issues.Add(Issue("raid.confirmation_required", "confirmationPhrase", "必须输入确认短语"));
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            issues.Add(Issue("raid.idempotency_key_invalid", "idempotencyKey", "幂等键无效"));
        }
        return issues;
    }

    private static int? MinimumDevices(string? level) => level switch
    {
        "raid0" => 2,
        "raid1" => 2,
        "raid5" => 3,
        "raid6" => 4,
        "raid10" => 4,
        _ => null
    };

    private static string? NormalizeLevel(string? level)
    {
        var normalized = NullIfEmpty(level)?.ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "0" or "raid0" => "raid0",
            "1" or "raid1" => "raid1",
            "5" or "raid5" => "raid5",
            "6" or "raid6" => "raid6",
            "10" or "raid10" => "raid10",
            _ => null
        };
    }

    private static int? SyncPercentage(RaidArrayInformation array)
    {
        if (array.SyncCompletedSectors is null || array.SyncTotalSectors is null
            || array.SyncTotalSectors <= 0)
        {
            return null;
        }
        return (int)Math.Clamp(
            array.SyncCompletedSectors.Value * 100 / array.SyncTotalSectors.Value,
            0,
            100);
    }

    private static string? NullIfEmpty(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static RaidOperationIssue Issue(string code, string field, string message) =>
        new(code, field, message);

    private static RaidCommandRejected Reject(
        RaidCommandFailure failure,
        string code,
        IReadOnlyList<RaidOperationIssue>? issues = null,
        bool retryable = false) => new(failure, code, retryable, issues ?? []);

    private sealed record Normalization(
        RequestedRaidOperation? Request,
        IReadOnlyList<RaidOperationIssue> Issues);

    private sealed record Evaluation(
        RequestedRaidOperation Requested,
        RaidArrayInformation? Array,
        IReadOnlyList<string> MemberDeviceIds,
        IReadOnlyList<string> ResourceIds,
        string Fingerprint,
        IReadOnlyList<RaidOperationIssue> Issues,
        IReadOnlyList<string> Warnings);

    private enum ReconciliationState
    {
        Unknown,
        InProgress,
        Succeeded,
        Failed
    }

    private sealed record Reconciliation(
        ReconciliationState State,
        string? ArrayId,
        int? ProgressPercentage);
}
