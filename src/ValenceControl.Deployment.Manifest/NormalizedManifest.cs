using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;

namespace ValenceControl.Deployment.Manifest;

public sealed record NormalizedManifest(
    EnvironmentManifest Manifest,
    IReadOnlyCollection<DeploymentResource> Resources,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
}
