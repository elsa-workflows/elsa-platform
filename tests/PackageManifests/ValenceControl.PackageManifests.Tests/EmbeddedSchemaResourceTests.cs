
namespace ValenceControl.PackageManifests.Tests;

public sealed class EmbeddedSchemaResourceTests
{
    [Fact]
    public void V1_schema_is_embedded()
    {
        var resources = typeof(ElsaPackageManifest).Assembly.GetManifestResourceNames();

        Assert.Contains(resources, x => x.EndsWith("Schemas.elsa-package-manifest.v1.json", StringComparison.Ordinal));
    }
}
