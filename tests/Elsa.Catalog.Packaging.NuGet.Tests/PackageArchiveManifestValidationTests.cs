using Elsa.Catalog.Packaging.NuGet;
using Elsa.Catalog.Testing;
using FluentAssertions;

namespace Elsa.Catalog.Packaging.NuGet.Tests;

public sealed class PackageArchiveManifestValidationTests
{
    [Fact]
    public async Task Reports_missing_manifest_without_throwing()
    {
        await using var package = new NuGetPackageFixtureBuilder()
            .WithFile("readme.txt", "hello")
            .Build();

        var result = await new PackageArchiveManifestReader().ReadAsync(package);

        result.Exists.Should().BeFalse();
    }
}
