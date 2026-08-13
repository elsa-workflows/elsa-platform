namespace ValenceControl.PackageManifest.Generator.Core.Validation;

public static class GenerationDiagnosticFormatter
{
    public static string Format(GenerationDiagnostic diagnostic)
    {
        var target = string.IsNullOrWhiteSpace(diagnostic.Target) ? "" : $" [{diagnostic.Target}]";
        return $"{diagnostic.Code}{target}: {diagnostic.Message}";
    }
}
