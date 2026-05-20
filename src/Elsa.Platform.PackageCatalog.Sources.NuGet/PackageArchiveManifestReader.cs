using System.IO.Compression;
using System.Security.Cryptography;
using Elsa.Platform.PackageCatalog.Core.Packaging;

namespace Elsa.Platform.PackageCatalog.Sources.NuGet;

public sealed class PackageArchiveManifestReader : IPackageArchiveManifestReader
{
    public const string RootManifestPath = "elsa-package.json";
    public const string FallbackManifestPath = "build/elsa-package.json";

    public async Task<PackageManifestReadResult> ReadAsync(Stream packageStream, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries
            .Where(entry => string.Equals(Normalize(entry.FullName), RootManifestPath, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(Normalize(entry.FullName), FallbackManifestPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var selected = entries.FirstOrDefault(entry => string.Equals(Normalize(entry.FullName), RootManifestPath, StringComparison.OrdinalIgnoreCase))
                       ?? entries.FirstOrDefault(entry => string.Equals(Normalize(entry.FullName), FallbackManifestPath, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
            return PackageManifestReadResult.Missing();

        await using var manifestStream = selected.Open();
        using var memory = new MemoryStream();
        await manifestStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var warnings = entries.Count > 1
            ? new[] { "Multiple manifest files found; selected the root manifest when available." }
            : [];

        return PackageManifestReadResult.Found(Normalize(selected.FullName), json, hash, warnings);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
