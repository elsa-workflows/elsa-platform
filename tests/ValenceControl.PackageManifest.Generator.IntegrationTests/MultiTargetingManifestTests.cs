using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using FluentAssertions;

namespace ValenceControl.PackageManifest.Generator.IntegrationTests;

public sealed class MultiTargetingManifestTests
{
    [Fact]
    public void SelectCanonical_uses_first_declared_target_framework_for_equivalent_surfaces()
    {
        var diagnostics = new GenerationDiagnostics();
        var manifests = new Dictionary<string, string>
        {
            ["net10.0"] = ManifestWithSetting("Endpoint"),
            ["net8.0"] = ManifestWithSetting("Endpoint")
        };

        var selected = new MultiTargetManifestCoordinator().SelectCanonical(manifests, false, diagnostics);

        selected.Should().Be(manifests["net10.0"]);
        diagnostics.Items.Should().NotContain(x => x.Code == "EPMGEN_MULTITARGET_SURFACE_DIFFERS");
    }

    [Fact]
    public void SelectCanonical_reports_divergent_target_framework_surfaces()
    {
        var diagnostics = new GenerationDiagnostics();
        var manifests = new Dictionary<string, string>
        {
            ["net10.0"] = ManifestWithSetting("Endpoint"),
            ["net8.0"] = ManifestWithSetting("Other")
        };

        new MultiTargetManifestCoordinator().SelectCanonical(manifests, false, diagnostics);

        diagnostics.Items.Should().Contain(x => x.Code == "EPMGEN_MULTITARGET_SURFACE_DIFFERS" && x.Severity == GenerationDiagnosticSeverity.Error);
    }

    private static string ManifestWithSetting(string settingName) =>
        $$"""
        {
          "features": [
            {
              "id": "Sample.Feature",
              "settings": [
                {
                  "name": "{{settingName}}",
                  "clrType": "System.String",
                  "jsonType": "string",
                  "required": false,
                  "validation": {}
                }
              ]
            }
          ]
        }
        """;
}
