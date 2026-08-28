using ElsaControl.Deployment.Abstractions.Diagnostics;

namespace ElsaControl.Deployment.Manifest;

public sealed record ManifestParseResult(
    EnvironmentManifest? Manifest,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics)
{
    public bool Succeeded => Manifest is not null && Diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
}
