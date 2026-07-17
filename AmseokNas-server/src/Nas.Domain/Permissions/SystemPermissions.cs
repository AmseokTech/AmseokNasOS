//--------------------------//
//--------集中定义后端授权使用的权限点---------//
//--------Defines permission codes used by backend authorization--------//
//-------------------------//
namespace Nas.Domain.Permissions;

public static class SystemPermissions
{
    public static IReadOnlyList<string> All { get; } =
    [
        StorageRead,
        StorageWrite,
        StorageFormat,
        RaidManage,
        ShareRead,
        ShareManage,
        UserRead,
        UserManage,
        NetworkRead,
        NetworkManage,
        ServiceRead,
        ServiceManage,
        DockerRead,
        DockerManage,
        BackupRead,
        BackupManage,
        SystemReboot,
        SystemShutdown,
        LogsRead
    ];

    public const string StorageRead = "storage.read";
    public const string StorageWrite = "storage.write";
    public const string StorageFormat = "storage.format";
    public const string RaidManage = "raid.manage";
    public const string ShareRead = "share.read";
    public const string ShareManage = "share.manage";
    public const string UserRead = "user.read";
    public const string UserManage = "user.manage";
    public const string NetworkRead = "network.read";
    public const string NetworkManage = "network.manage";
    public const string ServiceRead = "service.read";
    public const string ServiceManage = "service.manage";
    public const string DockerRead = "docker.read";
    public const string DockerManage = "docker.manage";
    public const string BackupRead = "backup.read";
    public const string BackupManage = "backup.manage";
    public const string SystemReboot = "system.reboot";
    public const string SystemShutdown = "system.shutdown";
    public const string LogsRead = "logs.read";
}
