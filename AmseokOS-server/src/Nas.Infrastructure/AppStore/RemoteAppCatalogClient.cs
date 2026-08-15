//--------------------------//
//--------通过受限 HTTPS 文档读取远端应用目录---------//
//--------Reads the remote app catalog through constrained HTTPS documents--------//
//-------------------------//
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nas.Application.AppStore;

namespace Nas.Infrastructure.AppStore;

public sealed class RemoteAppCatalogClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AppStoreOptions> options) : IRemoteAppCatalogClient
{
    public const string HttpClientName = "app-store";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16
    };
    private readonly AppStoreOptions settings = options.Value;

    public async Task<RemoteAppCatalogFetchResult> FetchAsync(
        string? entityTag,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            throw Unavailable("app_catalog.disabled", "Remote app catalog is disabled");
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var channelRequest = new HttpRequestMessage(HttpMethod.Get, settings.ChannelPath);
        if (!string.IsNullOrWhiteSpace(entityTag)
            && EntityTagHeaderValue.TryParse(entityTag, out var parsedEntityTag))
        {
            channelRequest.Headers.IfNoneMatch.Add(parsedEntityTag);
        }

        using var channelResponse = await SendAsync(
            httpClient,
            channelRequest,
            cancellationToken);
        if (channelResponse.StatusCode == HttpStatusCode.NotModified)
        {
            return new(true, entityTag, null, null);
        }

        RequireSuccess(channelResponse);
        var channel = await ReadDocumentAsync<RemoteAppChannelDocument>(
            channelResponse,
            cancellationToken);
        var channelEntityTag = channelResponse.Headers.ETag?.ToString();
        var catalogPath = RequireCatalogPath(channel.CatalogPath, channel.Revision);

        using var catalogRequest = new HttpRequestMessage(
            HttpMethod.Get,
            catalogPath);
        using var catalogResponse = await SendAsync(
            httpClient,
            catalogRequest,
            cancellationToken);
        RequireSuccess(catalogResponse);
        var catalog = await ReadDocumentAsync<RemoteAppCatalogDocument>(
            catalogResponse,
            cancellationToken);

        return new(false, channelEntityTag, channel, catalog);
    }

    private static string RequireCatalogPath(string? path, string? revision)
    {
        var normalized = path?.TrimStart('/');
        if (!IsIdentifier(revision)
            || !string.Equals(
                normalized,
                $"manifests/v1/catalogs/{revision}/catalog.json",
                StringComparison.Ordinal))
        {
            // The pointer is untrusted; constrain the second request before HttpClient sees it.
            throw Unavailable(
                "app_catalog.invalid",
                "Remote channel catalogPath is invalid");
        }

        return normalized!;
    }

    private static bool IsIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 100
            && IsLowercaseAsciiLetterOrDigit(value[0])
            && IsLowercaseAsciiLetterOrDigit(value[^1])
            && value.All(character =>
                IsLowercaseAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
    }

    private static bool IsLowercaseAsciiLetterOrDigit(char value)
    {
        return char.IsAsciiLetterOrDigit(value) && !char.IsAsciiLetterUpper(value);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable("app_catalog.timeout", "Remote app catalog request timed out");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable(
                "app_catalog.unavailable",
                "Remote app catalog request failed",
                exception);
        }
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw Unavailable(
                "app_catalog.upstream_error",
                $"Remote app catalog returned HTTP {(int)response.StatusCode}");
        }
    }

    private async Task<T> ReadDocumentAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > settings.MaximumDocumentBytes)
        {
            throw Unavailable("app_catalog.too_large", "Remote app catalog document is too large");
        }

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var bounded = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (bounded.Length + read > settings.MaximumDocumentBytes)
                {
                    throw Unavailable(
                        "app_catalog.too_large",
                        "Remote app catalog document is too large");
                }

                await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            bounded.Position = 0;
            return await JsonSerializer.DeserializeAsync<T>(
                    bounded,
                    JsonOptions,
                    cancellationToken)
                ?? throw Unavailable("app_catalog.invalid", "Remote app catalog is empty");
        }
        catch (JsonException exception)
        {
            throw Unavailable(
                "app_catalog.invalid",
                "Remote app catalog contains invalid JSON",
                exception);
        }
        catch (AppCatalogUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw Unavailable(
                "app_catalog.unavailable",
                "Remote app catalog response could not be read",
                exception);
        }
    }

    private static AppCatalogUnavailableException Unavailable(
        string code,
        string message,
        Exception? innerException = null)
    {
        return new AppCatalogUnavailableException(code, message, innerException);
    }
}
