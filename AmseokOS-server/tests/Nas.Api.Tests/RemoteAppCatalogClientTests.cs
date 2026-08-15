//--------------------------//
//--------验证远端目录客户端不会跟随不可信清单地址---------//
//--------Verifies the remote catalog client never follows untrusted manifest addresses--------//
//-------------------------//
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Nas.Application.AppStore;
using Nas.Infrastructure.AppStore;

namespace Nas.Api.Tests;

public sealed class RemoteAppCatalogClientTests
{
    [Fact]
    public async Task AbsoluteCatalogPathIsRejectedBeforeASecondRequest()
    {
        var handler = new RecordingHandler(
            """
            {
              "format": "amseok-app-channel-v1",
              "channel": "stable",
              "revision": "revision-1",
              "catalogPath": "https://untrusted.example/catalog.json"
            }
            """);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://download.amseok.cn/")
        };
        var client = new RemoteAppCatalogClient(
            new HttpClientFactoryStub(httpClient),
            Options.Create(new AppStoreOptions()));

        var exception = await Assert.ThrowsAsync<AppCatalogUnavailableException>(
            () => client.FetchAsync(null, CancellationToken.None));

        Assert.Equal("app_catalog.invalid", exception.Code);
        Assert.Equal(
            ["https://download.amseok.cn/manifests/v1/channels/stable/current.json"],
            handler.RequestUris);
    }

    private sealed class HttpClientFactoryStub(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
