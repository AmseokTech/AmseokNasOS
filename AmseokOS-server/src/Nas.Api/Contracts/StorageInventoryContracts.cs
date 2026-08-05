//--------------------------//
//--------定义块设备与 RAID 阵列只读 HTTP 响应契约---------//
//--------Defines read-only HTTP responses for block devices and RAID arrays--------//
//-------------------------//
using Nas.Application.Storage;

namespace Nas.Api.Contracts;

public sealed record BlockDeviceResponse(
    string Id,
    bool Stable,
    bool IdentityConflict,
    bool TopologyComplete,
    string Name,
    string Path,
    string? Model,
    string? SerialNumber,
    string? Wwn,
    long SizeBytes,
    long LogicalSectorBytes,
    long PhysicalSectorBytes,
    bool Rotational,
    bool Removable,
    bool ReadOnly,
    IReadOnlyList<BlockPartitionResponse> Partitions,
    IReadOnlyList<string> MountPoints,
    bool SystemDevice,
    bool Swap,
    bool RaidMember,
    bool InUse,
    IReadOnlyList<BlockDependencyResponse> DependentDevices)
{
    public static BlockDeviceResponse From(BlockDeviceInformation information)
    {
        return new(
            information.Id,
            information.Stable,
            information.IdentityConflict,
            information.TopologyComplete,
            information.Name,
            information.Path,
            information.Model,
            information.SerialNumber,
            information.Wwn,
            information.SizeBytes,
            information.LogicalSectorBytes,
            information.PhysicalSectorBytes,
            information.Rotational,
            information.Removable,
            information.ReadOnly,
            information.Partitions.Select(BlockPartitionResponse.From).ToArray(),
            information.MountPoints,
            information.SystemDevice,
            information.Swap,
            information.RaidMember,
            information.InUse,
            (information.DependentDevices ?? [])
                .Select(BlockDependencyResponse.From)
                .ToArray());
    }
}

public sealed record BlockPartitionResponse(
    string Name,
    string Path,
    long SizeBytes,
    IReadOnlyList<string> MountPoints,
    bool TopologyComplete,
    bool SystemDevice,
    bool Swap,
    bool RaidMember,
    bool InUse,
    IReadOnlyList<BlockDependencyResponse> DependentDevices)
{
    public static BlockPartitionResponse From(BlockPartitionInformation information)
    {
        return new(
            information.Name,
            information.Path,
            information.SizeBytes,
            information.MountPoints,
            information.TopologyComplete,
            information.SystemDevice,
            information.Swap,
            information.RaidMember,
            information.InUse,
            (information.DependentDevices ?? [])
                .Select(BlockDependencyResponse.From)
                .ToArray());
    }
}

public sealed record BlockDependencyResponse(
    string Name,
    string Path,
    string Kind,
    IReadOnlyList<string> MountPoints,
    bool Swap)
{
    public static BlockDependencyResponse From(BlockDependencyInformation information)
    {
        return new(
            information.Name,
            information.Path,
            information.Kind,
            information.MountPoints,
            information.Swap);
    }
}

public sealed record RaidArrayResponse(
    string Id,
    string Name,
    string Path,
    string? Uuid,
    string Level,
    string State,
    string? MetadataVersion,
    long SizeBytes,
    long ConfiguredDeviceCount,
    long DegradedDeviceCount,
    string SyncAction,
    long? SyncCompletedSectors,
    long? SyncTotalSectors,
    IReadOnlyList<RaidMemberResponse> Members)
{
    public static RaidArrayResponse From(RaidArrayInformation information)
    {
        return new(
            information.Id,
            information.Name,
            information.Path,
            information.Uuid,
            information.Level,
            information.State,
            information.MetadataVersion,
            information.SizeBytes,
            information.ConfiguredDeviceCount,
            information.DegradedDeviceCount,
            information.SyncAction,
            information.SyncCompletedSectors,
            information.SyncTotalSectors,
            information.Members.Select(RaidMemberResponse.From).ToArray());
    }
}

public sealed record RaidMemberResponse(
    string Name,
    string Path,
    string State,
    int? Slot)
{
    public static RaidMemberResponse From(RaidMemberInformation information)
    {
        return new(information.Name, information.Path, information.State, information.Slot);
    }
}
