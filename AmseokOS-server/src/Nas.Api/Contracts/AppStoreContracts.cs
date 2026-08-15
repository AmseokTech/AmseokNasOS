//--------------------------//
//--------定义应用市场只读 HTTP 响应契约---------//
//--------Defines the read-only app-store HTTP response contract--------//
//-------------------------//
using Nas.Application.AppStore;

namespace Nas.Api.Contracts;

public sealed record AppCatalogResponse(
    string Format,
    string Revision,
    DateTimeOffset GeneratedAt,
    DateTimeOffset RefreshedAt,
    bool IsStale,
    IReadOnlyList<AppCatalogEntryResponse> Apps)
{
    public static AppCatalogResponse From(AppCatalogSnapshot snapshot)
    {
        return new(
            snapshot.Catalog.Format,
            snapshot.Catalog.Revision,
            snapshot.Catalog.GeneratedAt,
            snapshot.RefreshedAt,
            snapshot.IsStale,
            snapshot.Catalog.Apps.Select(AppCatalogEntryResponse.From).ToArray());
    }
}

public sealed record AppCatalogEntryResponse(
    string PublisherId,
    string Id,
    string Name,
    string Category,
    string Eyebrow,
    string Description,
    string Overview,
    IReadOnlyList<string> Features,
    string ImageUrl)
{
    public static AppCatalogEntryResponse From(AppCatalogEntry entry)
    {
        return new(
            entry.PublisherId,
            entry.Id,
            entry.Name,
            entry.Category,
            entry.Eyebrow,
            entry.Description,
            entry.Overview,
            entry.Features,
            entry.ImageUrl);
    }
}
