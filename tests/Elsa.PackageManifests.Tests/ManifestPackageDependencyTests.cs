using FluentAssertions;

namespace Elsa.PackageManifests.Tests;

public sealed class ManifestPackageDependencyTests
{
    [Fact]
    public void Manifest_package_does_not_reference_catalog_or_runtime_internals()
    {
        var references = typeof(ElsaPackageManifest).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToList();

        references.Where(name => name is not null && name.StartsWith("Elsa.Catalog", StringComparison.Ordinal)).Should().BeEmpty();
        references.Where(name => name is not null && name.Contains("Nuplane", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }
}
