//--------------------------//
//--------编排只读系统设置查询且不接触 Linux 实现细节---------//
//--------Orchestrates read-only settings queries without Linux implementation details--------//
//-------------------------//
namespace Nas.Application.SystemSettings;

public sealed class SystemSettingsService(IPrivilegedClient privilegedClient)
    : ISystemSettingsService
{
    public Task<SystemAboutInformation> GetAboutAsync(CancellationToken cancellationToken)
    {
        return privilegedClient.GetAboutAsync(cancellationToken);
    }

    public Task<IReadOnlyList<NetworkInterfaceInformation>> GetNetworkInterfacesAsync(
        CancellationToken cancellationToken)
    {
        return privilegedClient.GetNetworkInterfacesAsync(cancellationToken);
    }
}
