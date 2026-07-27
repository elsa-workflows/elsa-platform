using System.Text.Json;
using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;

namespace ValenceControl.PackageManifests.Tests;

public sealed class ManifestSerializationTests
{
    [Fact]
    public void Deserialization_preserves_unknown_extension_data()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "x-vendor": { "enabled": true }
        }
        """;

        var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(json, ManifestJsonSerializerOptions.Default);

        Assert.NotNull(manifest);
        Assert.Contains("x-vendor", manifest!.ExtensionData.Keys);
    }

    [Fact]
    public void Runtime_kind_compatibility_round_trips()
    {
        var manifest = new ElsaPackageManifest
        {
            Package = new PackageIdentityManifest { Id = "Elsa.Mixed", Version = "1.0.0" },
            DisplayName = "Mixed",
            Compatibility = new CompatibilityManifest { RuntimeKinds = [ElsaRuntimeKinds.Server, "acme.custom-host"] },
            Features =
            [
                new FeatureManifest
                {
                    Id = "studio-widget",
                    TypeName = "Elsa.Mixed.StudioWidgetFeature",
                    DisplayName = "Studio Widget",
                    Compatibility = new CompatibilityManifest { RuntimeKinds = [ElsaRuntimeKinds.Studio] }
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest, ManifestJsonSerializerOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<ElsaPackageManifest>(json, ManifestJsonSerializerOptions.Default);

        Assert.Equal(new[] { ElsaRuntimeKinds.Server, "acme.custom-host" }.Order(), roundTripped!.Compatibility!.RuntimeKinds.Order());

        Assert.Equal(new[] { ElsaRuntimeKinds.Studio }.Order(), roundTripped.Features[0].Compatibility!.RuntimeKinds.Order());

    }
}
