using Elsa.Platform.Deployment.Abstractions.Diagnostics;

namespace Elsa.Platform.Deployment.Artifacts;

internal static class DeploymentArtifactPathValidator
{
    public static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
            return null;

        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            return null;

        return string.Join('/', segments);
    }

    public static DeploymentDiagnostic Invalid(string path, string message) =>
        new(
            ArtifactDiagnosticCodes.PathInvalid,
            DeploymentDiagnosticSeverity.Error,
            message,
            details: new Dictionary<string, string> { ["path"] = path });
}
