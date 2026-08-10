//--------------------------//
//--------定义块设备与 RAID 阵列只读查询边界---------//
//--------Defines read-only block-device and RAID-array query boundaries--------//
//-------------------------//
namespace Nas.Application.Storage;

public sealed record BlockDeviceInformation(
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
    IReadOnlyList<BlockPartitionInformation> Partitions,
    IReadOnlyList<string> MountPoints,
    bool SystemDevice,
    bool Swap,
    bool RaidMember,
    bool InUse,
    IReadOnlyList<BlockDependencyInformation> DependentDevices);

public sealed record BlockPartitionInformation(
    string Name,
    string Path,
    long SizeBytes,
    IReadOnlyList<string> MountPoints,
    bool TopologyComplete,
    bool SystemDevice,
    bool Swap,
    bool RaidMember,
    bool InUse,
    IReadOnlyList<BlockDependencyInformation> DependentDevices);

public sealed record BlockDependencyInformation(
    string Name,
    string Path,
    string Kind,
    IReadOnlyList<string> MountPoints,
    bool Swap);

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

public sealed record DiskSmartInformation(
    string DeviceId,
    bool Supported,
    bool Enabled,
    string Status,
    bool? Passed,
    long? TemperatureCelsius,
    ulong? PowerOnHours,
    ulong? PowerCycleCount,
    ulong? ReallocatedSectorCount,
    ulong? PendingSectorCount,
    ulong? OfflineUncorrectableSectorCount,
    ulong? MediaErrorCount,
    ulong? PercentageUsed,
    ulong? CriticalWarning);

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

public interface IDiskSmartService
{
    Task<DiskSmartInformation> GetDiskSmartAsync(
        string deviceId,
        CancellationToken cancellationToken);
}

public interface IDiskSmartClient
{
    Task<DiskSmartInformation> GetDiskSmartAsync(
        string deviceId,
        CancellationToken cancellationToken);
}
