using ElsaControl.PackageCatalog.Sources.NuGet;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.PackageCatalog.Sources.NuGet.Tests;

public sealed class PackageArchiveManifestReaderTests
{
    [Fact]
    public async Task Reads_root_manifest_before_fallback_manifest()
    {
        await using var package = new NuGetPackageFixtureBuilder()
            .WithFile("build/elsa-package.json", """{"source":"fallback"}""")
            .WithManifest("""{"source":"root"}""")
            .Build();

        var result = await new PackageArchiveManifestReader().ReadAsync(package);

        Assert.True(result.Exists);
        Assert.Equal("elsa-package.json", result.Path);
        Assert.Equal("""{"source":"root"}""", result.ManifestJson);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task Reads_fallback_manifest_when_root_is_missing()
    {
        await using var package = new NuGetPackageFixtureBuilder()
            .WithManifest("""{"source":"fallback"}""", "build/elsa-package.json")
            .Build();

        var result = await new PackageArchiveManifestReader().ReadAsync(package);

        Assert.True(result.Exists);
        Assert.Equal("build/elsa-package.json", result.Path);
    }
}
