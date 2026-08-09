//--------------------------//
//--------定义 RAID 生命周期预检与操作 HTTP 契约---------//
//--------Defines HTTP contracts for RAID lifecycle previews and operations--------//
//-------------------------//
using System.ComponentModel.DataAnnotations;
using Nas.Application.RaidManagement;

namespace Nas.Api.Contracts;

public sealed record CreateRaidOperationPreviewRequest(
    [param: Required, MaxLength(32)] string Action,
    [param: MaxLength(300)] string? ArrayId,
    [param: MaxLength(32)] string? ArrayName,
    [param: MaxLength(16)] string? Level,
    IReadOnlyList<string>? DeviceIds,
    [param: MaxLength(300)] string? SourceDeviceId,
    int? TargetDeviceCount,
    [param: Required, MaxLength(256)] string Password);

public sealed record ExecuteRaidOperationRequest(
    [param: Required, MaxLength(256)] string PreviewToken,
    [param: Required, MaxLength(256)] string ConfirmationPhrase,
    [param: Required, MaxLength(200)] string IdempotencyKey,
    [param: Required, MaxLength(256)] string Password);

public sealed record RaidOperationIssueResponse(string Code, string Field, string Message)
{
    public static RaidOperationIssueResponse From(RaidOperationIssue issue) =>
        new(issue.Code, issue.Field, issue.Message);
}

public sealed record RaidOperationPreviewResponse(
    string Action,
    string? ArrayId,
    string? ArrayName,
    string? Level,
    IReadOnlyList<string> DeviceIds,
    string? SourceDeviceId,
    int? TargetDeviceCount,
    string? ArrayDisplayName,
    IReadOnlyList<string> ExpectedMemberDeviceIds,
    bool CanExecute,
    string? PreviewToken,
    DateTimeOffset? ExpiresAt,
    string ConfirmationPhrase,
    IReadOnlyList<RaidOperationIssueResponse> BlockingIssues,
    IReadOnlyList<string> Warnings)
{
    public static RaidOperationPreviewResponse From(RaidOperationPreview preview) =>
        new(
            ActionName(preview.Requested.Kind),
            preview.Requested.ArrayId,
            preview.Requested.ArrayName,
            preview.Requested.Level,
            preview.Requested.DeviceIds,
            preview.Requested.SourceDeviceId,
            preview.Requested.TargetDeviceCount,
            preview.ArrayDisplayName,
            preview.ExpectedMemberDeviceIds,
            preview.CanExecute,
            preview.PreviewToken,
            preview.ExpiresAt,
            preview.ConfirmationPhrase,
            preview.BlockingIssues.Select(RaidOperationIssueResponse.From).ToArray(),
            preview.Warnings);

    public static string ActionName(RaidOperationKind kind) => kind switch
    {
        RaidOperationKind.Create => "create",
        RaidOperationKind.Delete => "delete",
        RaidOperationKind.AddDevice => "addDevice",
        RaidOperationKind.RemoveDevice => "removeDevice",
        RaidOperationKind.ReplaceDevice => "replaceDevice",
        RaidOperationKind.Grow => "grow",
        RaidOperationKind.Shrink => "shrink",
        _ => throw new InvalidOperationException("Unknown RAID action")
    };
}

public sealed record RaidOperationResponse(
    Guid OperationId,
    string Action,
    string Status,
    string ResourceId,
    string? ArrayId,
    string? ErrorCode,
    bool Retryable,
    int? ProgressPercentage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static RaidOperationResponse From(RaidOperation operation) =>
        new(
            operation.Id,
            RaidOperationPreviewResponse.ActionName(operation.Kind),
            operation.Status.ToString().ToLowerInvariant(),
            operation.ResourceId,
            operation.ArrayId,
            operation.ErrorCode,
            operation.Retryable,
            operation.ProgressPercentage,
            operation.CreatedAt,
            operation.UpdatedAt);
}
