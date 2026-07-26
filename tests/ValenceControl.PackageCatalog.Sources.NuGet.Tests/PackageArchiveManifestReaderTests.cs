using ValenceControl.PackageCatalog.Sources.NuGet;
using ValenceControl.PackageCatalog.Testing;
using FluentAssertions;

namespace ValenceControl.PackageCatalog.Sources.NuGet.Tests;

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

        result.Exists.Should().BeTrue();
        result.Path.Should().Be("elsa-package.json");
        result.ManifestJson.Should().Be("""{"source":"root"}""");
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Reads_fallback_manifest_when_root_is_missing()
    {
        await using var package = new NuGetPackageFixtureBuilder()
            .WithManifest("""{"source":"fallback"}""", "build/elsa-package.json")
            .Build();

        var result = await new PackageArchiveManifestReader().ReadAsync(package);

        result.Exists.Should().BeTrue();
        result.Path.Should().Be("build/elsa-package.json");
    }
}
