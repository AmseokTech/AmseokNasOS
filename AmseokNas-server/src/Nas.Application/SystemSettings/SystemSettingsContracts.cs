//--------------------------//
//--------定义系统设置查询与特权客户端应用边界---------//
//--------Defines system-settings queries and the privileged-client boundary--------//
//-------------------------//
namespace Nas.Application.SystemSettings;

public sealed class PrivilegedOptions
{
    public const string SectionName = "Privileged";

    public bool Enabled { get; init; }
    public string SocketPath { get; init; } = "/run/amseoknas/privileged.sock";
    public int TimeoutSeconds { get; init; } = 5;
}

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

public interface IPrivilegedClient
{
    Task<SystemAboutInformation> GetAboutAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
        CancellationToken cancellationToken);
}

public sealed class PrivilegedClientException(
    string code,
    string message,
    bool retryable,
    Exception? innerException = null,
    string? diagnosticMessage = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public string? DiagnosticMessage { get; } = diagnosticMessage;
}
