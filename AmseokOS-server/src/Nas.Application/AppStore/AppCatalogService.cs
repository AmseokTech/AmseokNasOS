//--------------------------//
//--------校验远端应用目录并保留最后一次有效快照---------//
//--------Validates remote catalogs and preserves the last valid snapshot--------//
//-------------------------//
using System.Text.RegularExpressions;

namespace Nas.Application.AppStore;

public sealed partial class AppCatalogService(
    IRemoteAppCatalogClient remoteClient,
    AppCatalogPolicy policy,
    TimeProvider timeProvider) : IAppCatalogService
{
    private const string ChannelFormat = "amseok-app-channel-v1";
    private const string CatalogFormat = "amseok-app-catalog-v1";
    private const int MaximumAppCount = 2_000;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private AppCatalogSnapshot? snapshot;
    private DateTimeOffset? lastAttemptAt;
    private string? entityTag;
    private AppCatalogUnavailableException? lastFailure;

    public async Task<AppCatalogSnapshot> GetCatalogAsync(
        CancellationToken cancellationToken)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (lastAttemptAt is not null
                && now - lastAttemptAt < policy.MinimumRefreshInterval)
            {
                return CurrentSnapshotOrThrow();
            }

            lastAttemptAt = now;
            try
            {
                var result = await remoteClient.FetchAsync(entityTag, cancellationToken);
                if (result.NotModified)
                {
                    if (snapshot is null)
                    {
                        throw new AppCatalogUnavailableException(
                            "app_catalog.invalid_not_modified",
                            "Remote catalog returned not-modified before a valid snapshot existed");
                    }

                    snapshot = snapshot with { RefreshedAt = now, IsStale = false };
                }
                else
                {
                    snapshot = new AppCatalogSnapshot(
                        ValidateAndNormalize(result),
                        now,
                        false);
                    entityTag = result.EntityTag;
                }

                lastFailure = null;
                return snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AppCatalogUnavailableException exception)
            {
                lastFailure = exception;
                return CurrentSnapshotOrThrow();
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private AppCatalogSnapshot CurrentSnapshotOrThrow()
    {
        if (snapshot is not null)
        {
            return snapshot with { IsStale = lastFailure is not null };
        }

        throw lastFailure ?? new AppCatalogUnavailableException(
            "app_catalog.unavailable",
            "No valid application catalog is available");
    }

    private AppCatalog ValidateAndNormalize(RemoteAppCatalogFetchResult result)
    {
        var channel = result.Channel ?? throw Invalid("Channel document is missing");
        var catalog = result.Catalog ?? throw Invalid("Catalog document is missing");

        RequireEqual(channel.Format, ChannelFormat, "channel format");
        RequireEqual(channel.Channel, "stable", "channel name");
        RequireIdentifier(channel.Revision, "channel revision");
        RequireCatalogPath(channel.CatalogPath, channel.Revision);
        RequireEqual(catalog.Format, CatalogFormat, "catalog format");
        RequireEqual(catalog.Revision, channel.Revision, "catalog revision");

        if (catalog.GeneratedAt == default
            || catalog.GeneratedAt > timeProvider.GetUtcNow().AddMinutes(5))
        {
            throw Invalid("Catalog generatedAt is invalid");
        }

        var remoteApps = catalog.Apps ?? throw Invalid("Catalog apps are missing");
        if (remoteApps.Count > MaximumAppCount)
        {
            throw Invalid($"Catalog exceeds {MaximumAppCount} applications");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        var apps = new AppCatalogEntry[remoteApps.Count];
        for (var index = 0; index < remoteApps.Count; index++)
        {
            var app = remoteApps[index]
                ?? throw Invalid("Catalog contains a null application");
            RequireIdentifier(app.PublisherId, "publisherId");
            RequireIdentifier(app.Id, "app id");
            RequireLength(app.Name, 1, 120, "name");
            RequireCategory(app.Category);
            RequireLength(app.Eyebrow, 1, 60, "eyebrow");
            RequireLength(app.Description, 1, 240, "description");
            RequireLength(app.Overview, 1, 2_000, "overview");

            var features = app.Features;
            if (features is null
                || features.Count is < 1 or > 20
                || features.Any(feature =>
                    string.IsNullOrWhiteSpace(feature) || feature.Length > 240))
            {
                throw Invalid("Application features are invalid");
            }

            if (!identities.Add($"{app.PublisherId}/{app.Id}"))
            {
                throw Invalid("Catalog contains a duplicate application identity");
            }

            apps[index] = new AppCatalogEntry(
                app.PublisherId!,
                app.Id!,
                app.Name!.Trim(),
                app.Category!,
                app.Eyebrow!.Trim(),
                app.Description!.Trim(),
                app.Overview!.Trim(),
                features.Select(feature => feature!.Trim()).ToArray(),
                NormalizeAssetUrl(app.ImagePath));
        }

        return new AppCatalog(
            CatalogFormat,
            catalog.Revision!,
            catalog.GeneratedAt,
            apps);
    }

    private string NormalizeAssetUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw Invalid("Application imagePath is missing");
        }

        var normalized = path.TrimStart('/');
        if (!normalized.StartsWith("assets/", StringComparison.Ordinal)
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('/' or '.' or '_' or '-'))
            || !IsSafeRelativePath(normalized))
        {
            throw Invalid("Application imagePath is outside the assets directory");
        }

        var resolved = new Uri(policy.DistributionBaseUrl, normalized);
        if (resolved.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                resolved.Host,
                policy.DistributionBaseUrl.Host,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Application imagePath resolves outside the distribution host");
        }

        return resolved.AbsoluteUri;
    }

    private static void RequireCatalogPath(string? path, string? revision)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(revision))
        {
            throw Invalid("Channel catalogPath is missing");
        }

        var normalized = path.TrimStart('/');
        var expected = $"manifests/v1/catalogs/{revision}/catalog.json";
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
        {
            throw Invalid("Channel catalogPath is invalid");
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        return Uri.TryCreate(path, UriKind.Relative, out _)
            && !path.Contains('\\', StringComparison.Ordinal)
            && !path.Contains("..", StringComparison.Ordinal)
            && !path.Contains('?', StringComparison.Ordinal)
            && !path.Contains('#', StringComparison.Ordinal);
    }

    private static void RequireIdentifier(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 100
            || !IdentifierPattern().IsMatch(value))
        {
            throw Invalid($"Catalog {field} is invalid");
        }
    }

    private static void RequireCategory(string? value)
    {
        if (value is not ("create" or "work" or "tools" or "development"))
        {
            throw Invalid("Application category is invalid");
        }
    }

    private static void RequireLength(string? value, int minimum, int maximum, string field)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < minimum || length > maximum)
        {
            throw Invalid($"Application {field} is invalid");
        }
    }

    private static void RequireEqual(string? actual, string? expected, string field)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Invalid($"Remote {field} is unsupported");
        }
    }

    private static AppCatalogUnavailableException Invalid(string message)
    {
        return new AppCatalogUnavailableException("app_catalog.invalid", message);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
