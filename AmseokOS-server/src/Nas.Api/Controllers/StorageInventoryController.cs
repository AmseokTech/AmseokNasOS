//--------------------------//
//--------鉴权并转换块设备与 RAID 阵列只读查询---------//
//--------Authorizes and translates read-only block-device and RAID-array queries--------//
//-------------------------//
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nas.Api.Contracts;
using Nas.Application.Authentication;
using Nas.Application.Privileged;
using Nas.Application.Storage;

namespace Nas.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class StorageInventoryController(
    IStorageInventoryService inventory,
    IDiskSmartService diskSmart,
    ILogger<StorageInventoryController> logger) : ControllerBase
{
    [HttpGet("storage/disks")]
    [Authorize(Policy = AuthenticationDefaults.StorageReadPolicy)]
    [ProducesResponseType<IReadOnlyList<BlockDeviceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<BlockDeviceResponse>>> GetBlockDevices(
        CancellationToken cancellationToken)
    {
        try
        {
            var devices = await inventory.GetBlockDevicesAsync(cancellationToken);
            return Ok(devices.Select(BlockDeviceResponse.From).ToArray());
        }
        catch (PrivilegedClientException exception)
        {
            return PrivilegedUnavailable(exception);
        }
    }

    [HttpGet("raid/arrays")]
    [Authorize(Policy = AuthenticationDefaults.StorageReadPolicy)]
    [ProducesResponseType<IReadOnlyList<RaidArrayResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<RaidArrayResponse>>> GetRaidArrays(
        CancellationToken cancellationToken)
    {
        try
        {
            var arrays = await inventory.GetRaidArraysAsync(cancellationToken);
            return Ok(arrays.Select(RaidArrayResponse.From).ToArray());
        }
        catch (PrivilegedClientException exception)
        {
            return PrivilegedUnavailable(exception);
        }
    }

    [HttpGet("storage/disks/smart")]
    [Authorize(Policy = AuthenticationDefaults.StorageReadPolicy)]
    [ProducesResponseType<DiskSmartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DiskSmartResponse>> GetDiskSmart(
        [FromQuery] string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var information = await diskSmart.GetDiskSmartAsync(deviceId, cancellationToken);
            return Ok(DiskSmartResponse.From(information));
        }
        catch (PrivilegedClientException exception)
        {
            return SmartFailure(exception);
        }
    }

    private ObjectResult SmartFailure(PrivilegedClientException exception)
    {
        var statusCode = exception.Code switch
        {
            "request.invalid" => StatusCodes.Status400BadRequest,
            "resource.not_found" => StatusCodes.Status404NotFound,
            "resource.identity_unstable" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        logger.LogWarning(
            exception.InnerException,
            "Disk SMART query failed with {ErrorCode}; retryable: {Retryable}; diagnostic: {DiagnosticMessage}",
            exception.Code,
            exception.Retryable,
            exception.DiagnosticMessage);
        return Problem(
            statusCode: statusCode,
            title: "SMART 信息不可用",
            detail: PublicErrorMessage(exception.Code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.Code,
                ["retryable"] = exception.Retryable
            });
    }

    private ObjectResult PrivilegedUnavailable(PrivilegedClientException exception)
    {
        logger.LogWarning(
            exception.InnerException,
            "Storage inventory query failed with {ErrorCode}; retryable: {Retryable}; diagnostic: {DiagnosticMessage}",
            exception.Code,
            exception.Retryable,
            exception.DiagnosticMessage);
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "存储信息暂不可用",
            detail: PublicErrorMessage(exception.Code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.Code,
                ["retryable"] = exception.Retryable
            });
    }

    private static string PublicErrorMessage(string code)
    {
        return code switch
        {
            "privileged.disabled" => "底层存储查询服务尚未启用",
            "privileged.unavailable" => "底层存储查询服务当前不可用",
            "protocol.unsupported_version" => "底层存储查询服务协议不兼容",
            "request.deadline_exceeded" => "底层存储查询已超时",
            "inventory.read_failed" => "底层存储信息读取失败",
            "request.invalid" => "磁盘标识无效",
            "resource.not_found" => "目标磁盘不存在或已经移除",
            "resource.identity_unstable" => "目标磁盘身份不稳定，已拒绝读取",
            "smart.tool_not_available" => "SMART 查询工具尚未安装",
            "smart.tool_timeout" => "SMART 查询已超时",
            "smart.query_failed" => "SMART 信息读取失败",
            "smart.invalid_output" => "SMART 查询返回了无效数据",
            _ => "底层存储查询未能完成"
        };
    }
}
