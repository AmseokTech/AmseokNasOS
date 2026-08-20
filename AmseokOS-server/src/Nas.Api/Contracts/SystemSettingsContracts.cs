//--------------------------//
//--------定义本机性能与网络只读 HTTP 响应契约---------//
//--------Defines read-only HTTP responses for system performance and network settings--------//
//-------------------------//
using Nas.Application.SystemSettings;

namespace Nas.Api.Contracts;

public sealed record SystemAboutResponse(
    string HostName,
    string OperatingSystem,
    string KernelVersion,
    long UptimeSeconds,
    CpuInformation Cpu,
    MemoryInformation Memory,
    SystemStorageInformation SystemStorage)
{
    public static SystemAboutResponse From(SystemAboutInformation information)
    {
        return new(
            information.HostName,
            information.OperatingSystem,
            information.KernelVersion,
            information.UptimeSeconds,
            information.Cpu,
            information.Memory,
            information.SystemStorage);
    }
}

public sealed record SystemPerformanceResponse(
    long CapturedAtUnixMilliseconds,
    CpuPerformanceInformation Cpu,
    MemoryPerformanceInformation Memory,
    IReadOnlyList<DiskPerformanceInformation> Disks,
    IReadOnlyList<NetworkPerformanceInformation> Networks,
    IReadOnlyList<GpuPerformanceInformation> Gpus)
{
    public static SystemPerformanceResponse From(SystemPerformanceInformation information)
    {
        return new(
            information.CapturedAtUnixMilliseconds,
            information.Cpu,
            information.Memory,
            information.Disks,
            information.Networks,
            information.Gpus);
    }
}

public sealed record NetworkInterfaceResponse(
    string Id,
    string Name,
    string? Model,
    string? Driver,
    string MacAddress,
    string LinkState,
    long? SpeedMbps,
    string? Duplex,
    long Mtu,
    string ConfigurationMode,
    IReadOnlyList<string> Addresses,
    string? Gateway,
    IReadOnlyList<string> DnsServers)
{
    public static NetworkInterfaceResponse From(NetworkInterfaceInformation information)
    {
        return new(
            information.Id,
            information.Name,
            information.Model,
            information.Driver,
            information.MacAddress,
            information.LinkState,
            information.SpeedMbps,
            information.Duplex,
            information.Mtu,
            information.ConfigurationMode,
            information.Addresses,
            information.Gateway,
            information.DnsServers);
    }
}
