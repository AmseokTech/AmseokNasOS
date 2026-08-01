//--------------------------//
//--------定义系统设置查询与只读客户端应用边界---------//
//--------Defines system-settings queries and their read-only client boundary--------//
//-------------------------//
namespace Nas.Application.SystemSettings;

public sealed record SystemAboutInformation(
    string HostName,
    string OperatingSystem,
    string KernelVersion,
    long UptimeSeconds,
    CpuInformation Cpu,
    MemoryInformation Memory,
    SystemStorageInformation SystemStorage);

public sealed record CpuInformation(
    string Model,
    int PhysicalCoreCount,
    int LogicalProcessorCount,
    long? CurrentFrequencyMhz,
    long? MaximumFrequencyMhz);

public sealed record MemoryInformation(long TotalBytes);

public sealed record SystemStorageInformation(
    string Source,
    string? StableId,
    string? Model,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes);

public sealed record NetworkInterfaceInformation(
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
    IReadOnlyList<string> DnsServers);

public interface ISystemSettingsService
{
    Task<SystemAboutInformation> GetAboutAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
        CancellationToken cancellationToken);
}

public interface ISystemSettingsClient
{
    Task<SystemAboutInformation> GetAboutAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
        CancellationToken cancellationToken);
}
