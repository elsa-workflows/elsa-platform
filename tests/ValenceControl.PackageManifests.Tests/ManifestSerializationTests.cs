using System.Text.Json;
using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;
using FluentAssertions;

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

        manifest.Should().NotBeNull();
        manifest!.ExtensionData.Should().ContainKey("x-vendor");
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

        roundTripped!.Compatibility!.RuntimeKinds.Should().BeEquivalentTo(ElsaRuntimeKinds.Server, "acme.custom-host");
        roundTripped.Features[0].Compatibility!.RuntimeKinds.Should().BeEquivalentTo(ElsaRuntimeKinds.Studio);
    }
}
