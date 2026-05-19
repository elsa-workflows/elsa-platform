namespace Elsa.Platform.PackageCatalog.Abstractions.Compatibility;

public sealed record CompatibilityCheckRequest(
    string? ElsaVersion,
    string? DockerImageVersion,
    IReadOnlyList<SelectedPackageVersion> Packages,
    IReadOnlyList<string> Features,
    Guid? WorkspaceId = null);

public sealed record SelectedPackageVersion(Guid SourceId, string PackageId, string Version);

public sealed record CompatibilityCheckResult(bool Compatible, IReadOnlyList<CompatibilityFinding> Findings);

public sealed record CompatibilityFinding(string Severity, string Code, string Message)
{
    public static CompatibilityFinding Error(string code, string message) => new("error", code, message);
    public static CompatibilityFinding Warning(string code, string message) => new("warning", code, message);
}
