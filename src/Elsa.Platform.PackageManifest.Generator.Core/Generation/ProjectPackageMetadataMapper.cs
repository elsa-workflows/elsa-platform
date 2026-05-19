namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public static class ProjectPackageMetadataMapper
{
    public static ProjectPackageMetadata Map(
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
        string? targetFrameworks)
    {
        var resolvedPackageId = NonEmpty(packageId) ?? "";
        var resolvedVersion = NonEmpty(version) ?? "1.0.0";
        var frameworks = Split(targetFrameworks);
        if (frameworks.Count == 0 && !string.IsNullOrWhiteSpace(targetFramework))
            frameworks = [targetFramework.Trim()];

        return new ProjectPackageMetadata(
            resolvedPackageId,
            resolvedVersion,
            NonEmpty(title),
            NonEmpty(description),
            Split(authors),
            NonEmpty(repositoryUrl),
            NonEmpty(packageProjectUrl),
            Split(packageTags),
            NonEmpty(packageLicenseExpression),
            NonEmpty(packageReadmeFile),
            NonEmpty(targetFramework),
            frameworks.Order(StringComparer.Ordinal).ToArray());
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
