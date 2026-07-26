using ValenceControl.PackageManifest.Generator.Core.Validation;
using FluentAssertions;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class GenerationDiagnosticSeverityTests
{
    [Fact]
    public void Warning_severity_downgrades_mappable_validation_errors_without_failing()
    {
        var diagnostics = CreateManifestValidationDiagnostics();
        var policy = new ValidationSeverityPolicy("Warning", false);

        policy.MapLoggedSeverity(diagnostics.Items[0]).Severity.Should().Be(GenerationDiagnosticSeverity.Warning);
        policy.ShouldFail(diagnostics).Should().BeFalse();
    }

    [Fact]
    public void Warning_severity_fails_when_fail_on_warnings_is_enabled()
    {
        var diagnostics = CreateManifestValidationDiagnostics();

        new ValidationSeverityPolicy("Warning", true).ShouldFail(diagnostics).Should().BeTrue();
    }

    [Fact]
    public void Warning_severity_does_not_downgrade_fatal_failures()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Fatal("EPMGEN_ASSEMBLY_INVALID", "Assembly could not be inspected.");

        var policy = new ValidationSeverityPolicy("Warning", false);

        policy.MapLoggedSeverity(diagnostics.Items[0]).Severity.Should().Be(GenerationDiagnosticSeverity.Error);
        policy.ShouldFail(diagnostics).Should().BeTrue();
    }

    [Fact]
    public void None_severity_downgrades_non_fatal_non_mappable_errors()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Error("EPMGEN_SETTING_TYPE_UNSUPPORTED", "Setting type is unsupported.");
        var policy = new ValidationSeverityPolicy("None", false);

        policy.MapLoggedSeverity(diagnostics.Items[0]).Severity.Should().Be(GenerationDiagnosticSeverity.Info);
        policy.ShouldFail(diagnostics).Should().BeFalse();
    }

    [Fact]
    public void None_severity_does_not_downgrade_fatal_failures()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Fatal("EPMGEN_ASSEMBLY_INVALID", "Assembly could not be inspected.");
        var policy = new ValidationSeverityPolicy("None", false);

        policy.MapLoggedSeverity(diagnostics.Items[0]).Severity.Should().Be(GenerationDiagnosticSeverity.Error);
        policy.ShouldFail(diagnostics).Should().BeTrue();
    }

    private static GenerationDiagnostics CreateManifestValidationDiagnostics()
    {
        var diagnostics = new GenerationDiagnostics();
        diagnostics.Error(
            "EPMGEN_MANIFEST_INVALID",
            "Invalid manifest.",
            category: GenerationDiagnosticCategory.ManifestValidation,
            canMapValidationSeverity: true);
        return diagnostics;
    }
}
