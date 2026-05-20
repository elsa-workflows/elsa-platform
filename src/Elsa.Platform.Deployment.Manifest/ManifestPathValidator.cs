using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Manifest;

public static class ManifestPathValidator
{
    public static DeploymentDiagnostic? Validate(string? path, DeploymentResourceId resourceId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DeploymentDiagnostic(ManifestDiagnosticCodes.ResourcePathInvalid, DeploymentDiagnosticSeverity.Error, "Resource path is required.", resourceId);

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || normalized.Split('/').Any(segment => segment is "." or ".." or ""))
            return new DeploymentDiagnostic(ManifestDiagnosticCodes.ResourcePathInvalid, DeploymentDiagnosticSeverity.Error, $"Resource path '{path}' must stay within the manifest root.", resourceId);

        return null;
    }
}
