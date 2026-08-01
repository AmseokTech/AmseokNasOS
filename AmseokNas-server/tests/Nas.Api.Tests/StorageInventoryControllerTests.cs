//--------------------------//
//--------验证存储只读 API 的鉴权与响应映射---------//
//--------Verifies authorization and response mapping for storage read APIs--------//
//-------------------------//
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Nas.Api.Contracts;
using Nas.Api.Controllers;
using Nas.Application.Authentication;
using Nas.Application.Storage;

namespace Nas.Api.Tests;

public sealed class StorageInventoryControllerTests
{
    [Fact]
    public async Task BlockDevicesAreMappedWithoutDroppingSafetyFlags()
    {
        var service = new StorageInventoryServiceStub();
        var controller = new StorageInventoryController(
            service,
            NullLogger<StorageInventoryController>.Instance);

        var result = await controller.GetBlockDevices(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var devices = Assert.IsType<BlockDeviceResponse[]>(ok.Value);
        var device = Assert.Single(devices);
        Assert.Equal("wwn:test", device.Id);
        Assert.True(device.SystemDevice);
        Assert.False(device.RaidMember);
    }

    [Fact]
    public async Task RaidArraysAreMappedWithMemberAndSyncState()
    {
        var service = new StorageInventoryServiceStub();
        var controller = new StorageInventoryController(
            service,
            NullLogger<StorageInventoryController>.Instance);

        var result = await controller.GetRaidArrays(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var arrays = Assert.IsType<RaidArrayResponse[]>(ok.Value);
        var array = Assert.Single(arrays);
        Assert.Equal("raid1", array.Level);
        Assert.Equal(1024, array.SyncCompletedSectors);
        Assert.Equal("sda1", Assert.Single(array.Members).Name);
    }

    [Fact]
    public void BothEndpointsRequireStorageReadPolicy()
    {
        Assert.Equal(
            AuthenticationDefaults.StorageReadPolicy,
            PolicyFor(nameof(StorageInventoryController.GetBlockDevices)));
        Assert.Equal(
            AuthenticationDefaults.StorageReadPolicy,
            PolicyFor(nameof(StorageInventoryController.GetRaidArrays)));
    }

    private static string? PolicyFor(string methodName)
    {
        return typeof(StorageInventoryController)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)?
            .GetCustomAttribute<AuthorizeAttribute>()?
            .Policy;
    }

    private sealed class StorageInventoryServiceStub : IStorageInventoryService
    {
        public Task<IReadOnlyList<BlockDeviceInformation>> GetBlockDevicesAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<BlockDeviceInformation> devices =
            [
                new BlockDeviceInformation(
                    "wwn:test",
                    true,
                    "sda",
                    "/dev/sda",
                    "Test Disk",
                    "SERIAL",
                    "WWN",
                    1024,
                    512,
                    4096,
                    true,
                    false,
                    false,
                    [],
                    ["/"],
                    true,
                    false,
                    false)
            ];
            return Task.FromResult(devices);
        }

        public Task<IReadOnlyList<RaidArrayInformation>> GetRaidArraysAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<RaidArrayInformation> arrays =
            [
                new RaidArrayInformation(
                    "md:test",
                    "md0",
                    "/dev/md0",
                    "test",
                    "raid1",
                    "active",
                    "1.2",
                    1024,
                    2,
                    0,
                    "resync",
                    1024,
                    4096,
                    [new RaidMemberInformation("sda1", "/dev/sda1", "in_sync", 0)])
            ];
            return Task.FromResult(arrays);
        }
    }
}
