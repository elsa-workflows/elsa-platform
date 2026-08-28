using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Manifest;

public static class ManifestPathValidator
{
    public static DeploymentDiagnostic? Validate(string? path, DeploymentResourceId resourceId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DeploymentDiagnostic(ManifestDiagnosticCodes.ResourcePathRequired, DeploymentDiagnosticSeverity.Error, "Resource path is required.", resourceId);

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) ||
            Path.IsPathRooted(normalized) ||
            normalized.StartsWith('/') ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "." or ".." or ""))
            return new DeploymentDiagnostic(ManifestDiagnosticCodes.ResourcePathInvalid, DeploymentDiagnosticSeverity.Error, $"Resource path '{path}' must stay within the manifest root.", resourceId);

        return null;
    }
}
