//--------------------------//
//--------定义块设备与 RAID 阵列只读查询边界---------//
//--------Defines read-only block-device and RAID-array query boundaries--------//
//-------------------------//
namespace Nas.Application.Storage;

public sealed record BlockDeviceInformation(
    string Id,
    bool Stable,
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
    IReadOnlyList<BlockPartitionInformation> Partitions,
    IReadOnlyList<string> MountPoints,
    bool SystemDevice,
    bool Swap,
    bool RaidMember);

public sealed record BlockPartitionInformation(
    string Name,
    string Path,
    long SizeBytes,
    IReadOnlyList<string> MountPoints,
    bool Swap,
    bool RaidMember);

public sealed record RaidArrayInformation(
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
    IReadOnlyList<RaidMemberInformation> Members);

public sealed record RaidMemberInformation(
    string Name,
    string Path,
    string State,
    int? Slot);

public interface IStorageInventoryService
{
    Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(
        CancellationToken cancellationToken);
}

public interface IStorageInventoryClient
{
    Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(
        CancellationToken cancellationToken);
}
