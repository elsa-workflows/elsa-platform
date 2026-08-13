using ValenceControl.PackageCatalog.Sources.NuGet;
using ValenceControl.PackageCatalog.Testing;

namespace ValenceControl.PackageCatalog.Sources.NuGet.Tests;

public sealed class PackageArchiveManifestValidationTests
{
    [Fact]
    public async Task Reports_missing_manifest_without_throwing()
    {
        await using var package = new NuGetPackageFixtureBuilder()
            .WithFile("readme.txt", "hello")
            .Build();

        var result = await new PackageArchiveManifestReader().ReadAsync(package);

        Assert.False(result.Exists);
    }
}
