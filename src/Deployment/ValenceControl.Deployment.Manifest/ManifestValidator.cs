using ValenceControl.Deployment.Abstractions.Diagnostics;

namespace ValenceControl.Deployment.Manifest;

public static class ManifestValidator
{
    public static IReadOnlyCollection<DeploymentDiagnostic> ValidateManifestHeader(EnvironmentManifest manifest)
    {
        var diagnostics = new List<DeploymentDiagnostic>();

        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
            diagnostics.Add(Error(ManifestDiagnosticCodes.ApiVersionRequired, "Manifest apiVersion is required."));
        else if (!string.Equals(manifest.ApiVersion, DeploymentManifestConstants.ApiVersion, StringComparison.Ordinal))
            diagnostics.Add(Error(ManifestDiagnosticCodes.ApiVersionUnsupported, $"Manifest apiVersion '{manifest.ApiVersion}' is not supported."));

        if (string.IsNullOrWhiteSpace(manifest.Kind))
            diagnostics.Add(Error(ManifestDiagnosticCodes.KindRequired, "Manifest kind is required."));
        else if (!string.Equals(manifest.Kind, DeploymentManifestConstants.Kind, StringComparison.Ordinal))
            diagnostics.Add(Error(ManifestDiagnosticCodes.KindUnsupported, $"Manifest kind '{manifest.Kind}' is not supported."));

        if (manifest.Metadata is null || string.IsNullOrWhiteSpace(manifest.Metadata.Name))
            diagnostics.Add(Error(ManifestDiagnosticCodes.MetadataNameRequired, "Manifest metadata.name is required."));

        return diagnostics;
    }

    public static DeploymentDiagnostic Error(string code, string message) =>
        new(code, DeploymentDiagnosticSeverity.Error, message);
}
