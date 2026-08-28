using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Manifest;

public sealed record NormalizedManifest(
    EnvironmentManifest Manifest,
    IReadOnlyCollection<DeploymentResource> Resources,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
}
