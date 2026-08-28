using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.PackageCatalog.Core.Sources;

public sealed class PublicSourceQueryService(IPublicSourceQueries queries, PublicCatalogCache cache)
{
    public Task<IReadOnlyList<PublicSourceProjection>> ListSourcesAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("sources:list", queries.ListSourcesAsync, cancellationToken);
}

public interface IPublicSourceQueries
{
    Task<IReadOnlyList<PublicSourceProjection>> ListSourcesAsync(CancellationToken cancellationToken = default);
}

public sealed record PublicSourceProjection(
    Guid Id,
    string Name,
    string Url,
    int PackageCount);

public static class PublicSourceUrlSanitizer
{
    public static string Sanitize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "";

        var builder = new UriBuilder(uri)
        {
            UserName = "",
            Password = "",
            Query = "",
            Fragment = ""
        };

        return builder.Uri.ToString();
    }
}
