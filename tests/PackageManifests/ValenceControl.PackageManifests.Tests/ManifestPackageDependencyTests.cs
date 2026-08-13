
namespace ValenceControl.PackageManifests.Tests;

public sealed class ManifestPackageDependencyTests
{
    [Fact]
    public void Manifest_package_does_not_reference_catalog_or_runtime_internals()
    {
        var references = typeof(ElsaPackageManifest).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToList();

        Assert.DoesNotContain(references, name => name is not null && name.StartsWith("ValenceControl.PackageCatalog", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name is not null && name.Contains("Nuplane", StringComparison.OrdinalIgnoreCase));
    }
}
