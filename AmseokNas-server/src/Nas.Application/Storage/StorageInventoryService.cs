//--------------------------//
//--------编排存储实际状态查询且不接触 Linux 实现细节---------//
//--------Orchestrates storage-state queries without Linux implementation details--------//
//-------------------------//
namespace Nas.Application.Storage;

public sealed class StorageInventoryService(IStorageInventoryClient inventoryClient)
    : IStorageInventoryService
{
    public Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(
        CancellationToken cancellationToken)
    {
        return inventoryClient.GetBlockDevicesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(
        CancellationToken cancellationToken)
    {
        return inventoryClient.GetRaidArraysAsync(cancellationToken);
    }
}
