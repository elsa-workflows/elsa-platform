using System.IO.Compression;
using Elsa.Catalog.Packaging.NuGet;
using FluentAssertions;

namespace Elsa.Catalog.Packaging.NuGet.Tests;

public sealed class PackageArchiveManifestReaderSafetyTests
{
    [Fact]
    public async Task Reader_extracts_manifest_without_loading_package_assembly()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = archive.CreateEntry("elsa-package.json");
            await using (var writer = manifest.Open())
            await using (var text = new StreamWriter(writer))
            {
                await text.WriteAsync("{}");
            }

            archive.CreateEntry("lib/net10.0/Untrusted.dll");
        }

        stream.Position = 0;
        var result = await new PackageArchiveManifestReader().ReadAsync(stream);

        result.Exists.Should().BeTrue();
        result.ManifestJson.Should().Be("{}");
    }
}
