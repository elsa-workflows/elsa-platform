using ValenceControl.PackageManifest.Generator.Core.Validation;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class GeneratedManifestValidationTests
{
    [Fact]
    public void Default_policy_fails_required_manifest_validation_errors()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Error(
            "EPMGEN_MANIFEST_INVALID",
            "Package identity is required.",
            category: GenerationDiagnosticCategory.ManifestValidation,
            canMapValidationSeverity: true);

        Assert.True(new ValidationSeverityPolicy("Error", false).ShouldFail(diagnostics));
    }
}
