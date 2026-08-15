//--------------------------//
//--------定义远端应用市场只读连接配置---------//
//--------Defines the read-only remote app-store connection settings--------//
//-------------------------//
namespace Nas.Infrastructure.AppStore;

public sealed class AppStoreOptions
{
    public const string SectionName = "AppStore";

    public bool Enabled { get; init; } = true;

    public Uri BaseUrl { get; init; } = new("https://download.amseok.cn/");

    public string ChannelPath { get; init; } =
        "manifests/v1/channels/stable/current.json";

    public int RefreshSeconds { get; init; } = 30;

    public int TimeoutSeconds { get; init; } = 10;

    public int MaximumDocumentBytes { get; init; } = 1_048_576;
}
