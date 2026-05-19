namespace Elsa.PackageManifest.Generator.Core.Validation;

public sealed class ValidationSeverityPolicy(string validationSeverity, bool failOnWarnings)
{
    public bool ShouldFail(GenerationDiagnostics diagnostics)
    {
        var mappedDiagnostics = diagnostics.Items.Select(MapLoggedSeverity).ToArray();
        if (mappedDiagnostics.Any(x => x.IsFatal || x.Severity == GenerationDiagnosticSeverity.Error))
            return true;

        return failOnWarnings && mappedDiagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Warning);
    }

    public GenerationDiagnostic MapLoggedSeverity(GenerationDiagnostic diagnostic)
    {
        if (diagnostic.Severity != GenerationDiagnosticSeverity.Error || diagnostic.IsFatal)
            return diagnostic;

        if (string.Equals(validationSeverity, "None", StringComparison.OrdinalIgnoreCase))
            return diagnostic with { Severity = GenerationDiagnosticSeverity.Info };

        if (!diagnostic.CanMapValidationSeverity)
            return diagnostic;

        if (string.Equals(validationSeverity, "Warning", StringComparison.OrdinalIgnoreCase))
            return diagnostic with { Severity = GenerationDiagnosticSeverity.Warning };

        return diagnostic;
    }
}
