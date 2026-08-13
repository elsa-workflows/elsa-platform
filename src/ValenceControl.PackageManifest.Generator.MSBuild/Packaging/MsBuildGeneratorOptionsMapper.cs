using ValenceControl.PackageManifest.Generator.Core.Generation;

namespace ValenceControl.PackageManifest.Generator.MSBuild.Packaging;

public static class MsBuildGeneratorOptionsMapper
{
    public static GeneratorOptions MapOptions(
        string? outputPath,
        string? overrideFile,
        string? validationSeverity,
        string? strict,
        string? failOnWarnings,
        string? allowTargetFrameworkDifferences,
        string? diagnosticsVerbosity,
        string? additionalFeatureInterfaceTypes)
    {
        return new GeneratorOptions(
            true,
            string.IsNullOrWhiteSpace(outputPath) ? Path.Combine("obj", "elsa-package.json") : outputPath!,
            true,
            "elsa-package.json",
            string.IsNullOrWhiteSpace(overrideFile) ? null : overrideFile,
            string.IsNullOrWhiteSpace(validationSeverity) ? "Error" : validationSeverity!,
            ParseBool(strict),
            ParseBool(failOnWarnings),
            ParseBool(allowTargetFrameworkDifferences),
            string.IsNullOrWhiteSpace(diagnosticsVerbosity) ? "concise" : diagnosticsVerbosity!,
            Split(additionalFeatureInterfaceTypes));
    }

    public static ProjectPackageMetadata MapPackageMetadata(
        string? packageId,
        string? version,
        string? title,
        string? description,
        string? authors,
        string? repositoryUrl,
        string? packageProjectUrl,
        string? packageTags,
        string? packageLicenseExpression,
        string? packageReadmeFile,
        string? targetFramework,
        string? targetFrameworks) =>
        ProjectPackageMetadataMapper.Map(
            packageId,
            version,
            title,
            description,
            authors,
            repositoryUrl,
            packageProjectUrl,
            packageTags,
            packageLicenseExpression,
            packageReadmeFile,
            targetFramework,
            targetFrameworks);

    private static bool ParseBool(string? value) => bool.TryParse(value, out var result) && result;

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
