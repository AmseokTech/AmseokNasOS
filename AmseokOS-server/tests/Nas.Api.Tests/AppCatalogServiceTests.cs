//--------------------------//
//--------验证应用目录校验、缓存与降级边界---------//
//--------Verifies app-catalog validation, caching, and fallback boundaries--------//
//-------------------------//
using Nas.Application.AppStore;

namespace Nas.Api.Tests;

public sealed class AppCatalogServiceTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidCatalogIsNormalizedAndConditionalRefreshUsesTheCachedSnapshot()
    {
        var remote = new RemoteClientStub(
            ValidResult("\"revision-1\""),
            new RemoteAppCatalogFetchResult(true, "\"revision-1\"", null, null));
        var service = CreateService(remote);

        var first = await service.GetCatalogAsync(CancellationToken.None);
        var second = await service.GetCatalogAsync(CancellationToken.None);

        var app = Assert.Single(first.Catalog.Apps);
        Assert.Equal("amseok/photo-library", $"{app.PublisherId}/{app.Id}");
        Assert.Equal(
            "https://download.amseok.cn/assets/v1/publishers/amseok/apps/photo-library/card.jpg",
            app.ImageUrl);
        Assert.False(first.IsStale);
        Assert.False(second.IsStale);
        Assert.Equal([null, "\"revision-1\""], remote.EntityTags);
    }

    [Fact]
    public async Task InvalidRemoteCatalogCannotReplaceTheLastValidSnapshot()
    {
        var invalid = ValidResult("\"revision-2\"") with
        {
            Catalog = ValidResult("\"revision-2\"").Catalog! with
            {
                Apps =
                [
                    ValidResult("\"revision-2\"").Catalog!.Apps![0]! with
                    {
                        ImagePath = "https://untrusted.example/card.jpg"
                    }
                ]
            }
        };
        var remote = new RemoteClientStub(ValidResult("\"revision-1\""), invalid);
        var service = CreateService(remote);

        var first = await service.GetCatalogAsync(CancellationToken.None);
        var fallback = await service.GetCatalogAsync(CancellationToken.None);

        Assert.Equal("revision-1", first.Catalog.Revision);
        Assert.Equal("revision-1", fallback.Catalog.Revision);
        Assert.True(fallback.IsStale);
    }

    [Fact]
    public async Task InvalidInitialCatalogIsRejectedWithoutAFrontendSnapshot()
    {
        var invalid = ValidResult(null) with
        {
            Channel = ValidResult(null).Channel! with
            {
                CatalogPath = "https://untrusted.example/catalog.json"
            }
        };
        var service = CreateService(new RemoteClientStub(invalid));

        var exception = await Assert.ThrowsAsync<AppCatalogUnavailableException>(
            () => service.GetCatalogAsync(CancellationToken.None));

        Assert.Equal("app_catalog.invalid", exception.Code);
    }

    private static AppCatalogService CreateService(IRemoteAppCatalogClient remote)
    {
        return new AppCatalogService(
            remote,
            new AppCatalogPolicy(
                new Uri("https://download.amseok.cn/"),
                TimeSpan.Zero),
            new FixedTimeProvider(GeneratedAt.AddMinutes(1)));
    }

    private static RemoteAppCatalogFetchResult ValidResult(string? entityTag)
    {
        return new(
            false,
            entityTag,
            new RemoteAppChannelDocument(
                "amseok-app-channel-v1",
                "stable",
                "revision-1",
                "/manifests/v1/catalogs/revision-1/catalog.json"),
            new RemoteAppCatalogDocument(
                "amseok-app-catalog-v1",
                "revision-1",
                GeneratedAt,
                [
                    new RemoteAppCatalogEntry(
                        "amseok",
                        "photo-library",
                        "Photo Library",
                        "create",
                        "媒体管理",
                        "整理家庭影像",
                        "集中查看家庭照片和视频",
                        ["按时间线浏览", "相册管理"],
                        "/assets/v1/publishers/amseok/apps/photo-library/card.jpg")
                ]));
    }

    private sealed class RemoteClientStub(params object[] responses)
        : IRemoteAppCatalogClient
    {
        private readonly Queue<object> responses = new(responses);

        public List<string?> EntityTags { get; } = [];

        public Task<RemoteAppCatalogFetchResult> FetchAsync(
            string? entityTag,
            CancellationToken cancellationToken)
        {
            EntityTags.Add(entityTag);
            var response = responses.Dequeue();
            return response is Exception exception
                ? Task.FromException<RemoteAppCatalogFetchResult>(exception)
                : Task.FromResult((RemoteAppCatalogFetchResult)response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
