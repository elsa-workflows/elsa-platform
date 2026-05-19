namespace Elsa.PackageManifest.Generator.Core.Validation;

public static class MultiTargetingDiagnostics
{
    public static GenerationDiagnostic FeatureSurfaceDifference(string targetFramework) =>
        new(
            "EPMGEN_MULTITARGET_SURFACE_DIFFERS",
            GenerationDiagnosticSeverity.Error,
            $"Target framework '{targetFramework}' produced a different feature surface.",
            targetFramework);
}
