//--------------------------//
//--------定义数据库启动与节点文件安全选项---------//
//--------Defines database startup and node file security options--------//
//-------------------------//
namespace Nas.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public bool ApplyMigrationsOnStartup { get; init; }
}
