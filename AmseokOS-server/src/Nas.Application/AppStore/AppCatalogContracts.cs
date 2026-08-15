//--------------------------//
//--------定义应用市场目录查询及远端读取边界---------//
//--------Defines app-catalog queries and the remote read boundary--------//
//-------------------------//
namespace Nas.Application.AppStore;

public sealed record AppCatalog(
    string Format,
    string Revision,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AppCatalogEntry> Apps);

public sealed record AppCatalogEntry(
    string PublisherId,
    string Id,
    string Name,
    string Category,
    string Eyebrow,
    string Description,
    string Overview,
    IReadOnlyList<string> Features,
    string ImageUrl);

public sealed record AppCatalogSnapshot(
    AppCatalog Catalog,
    DateTimeOffset RefreshedAt,
    bool IsStale);

public sealed record RemoteAppChannelDocument(
    string? Format,
    string? Channel,
    string? Revision,
    string? CatalogPath);

public sealed record RemoteAppCatalogDocument(
    string? Format,
    string? Revision,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RemoteAppCatalogEntry?>? Apps);

public sealed record RemoteAppCatalogEntry(
    string? PublisherId,
    string? Id,
    string? Name,
    string? Category,
    string? Eyebrow,
    string? Description,
    string? Overview,
    IReadOnlyList<string?>? Features,
    string? ImagePath);

public sealed record RemoteAppCatalogFetchResult(
    bool NotModified,
    string? EntityTag,
    RemoteAppChannelDocument? Channel,
    RemoteAppCatalogDocument? Catalog);

public sealed record AppCatalogPolicy(
    Uri DistributionBaseUrl,
    TimeSpan MinimumRefreshInterval);

public interface IAppCatalogService
{
    Task<AppCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken);
}

public interface IRemoteAppCatalogClient
{
    Task<RemoteAppCatalogFetchResult> FetchAsync(
        string? entityTag,
        CancellationToken cancellationToken);
}

public sealed class AppCatalogUnavailableException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
