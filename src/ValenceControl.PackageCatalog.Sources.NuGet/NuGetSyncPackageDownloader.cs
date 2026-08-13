using ValenceControl.PackageCatalog.Core.Packaging;
using ValenceControl.PackageCatalog.Core.Packages;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace ValenceControl.PackageCatalog.Sources.NuGet;

public sealed class NuGetSyncPackageDownloader : IPackageArchiveDownloader
{
    public async Task<Stream> DownloadPackageAsync(PackageSource source, string packageId, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var repository = Repository.Factory.GetCoreV3(source.Url);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var stream = new MemoryStream();
        var success = await resource.CopyNupkgToStreamAsync(
            packageId,
            NuGetVersion.Parse(version),
            stream,
            new SourceCacheContext(),
            NullLogger.Instance,
            cancellationToken);

        if (!success)
            throw new InvalidOperationException($"Package {packageId} {version} could not be downloaded.");

        stream.Position = 0;
        return stream;
    }
}
