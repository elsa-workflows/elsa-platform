using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using Elsa.Platform.PackageManifest.Generator.Core.Overrides;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;
using FluentAssertions;

namespace Elsa.Platform.PackageManifest.Generator.Core.Tests;

public sealed class ManifestOverrideValidationTests
{
    [Fact]
    public void Validate_reports_feature_overrides_without_references()
    {
        var manifestOverride = new ManifestOverride
        {
            Features = [new FeatureOverride()]
        };
        var metadata = ProjectPackageMetadataMapper.Map("Sample.Elsa.Package", "1.2.3", null, null, null, null, null, null, null, null, "net10.0", null);
        var diagnostics = new GenerationDiagnostics();

        new ManifestOverrideValidator().Validate(manifestOverride, metadata, diagnostics);

        diagnostics.Items.Should().Contain(x => x.Code == "EPMGEN_OVERRIDE_FEATURE_ID");
    }

    [Fact]
    public void Warning_severity_does_not_downgrade_invalid_override_input_failures()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Fatal(
            "EPMGEN_OVERRIDE_INVALID",
            "Override JSON is invalid.",
            category: GenerationDiagnosticCategory.InvalidInput);

        new ValidationSeverityPolicy("Warning", false).ShouldFail(diagnostics).Should().BeTrue();
    }
}
