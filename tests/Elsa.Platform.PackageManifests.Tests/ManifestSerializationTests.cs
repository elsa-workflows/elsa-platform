using System.Text.Json;
using Elsa.Platform.PackageManifests;
using FluentAssertions;

namespace Elsa.Platform.PackageManifests.Tests;

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
}
