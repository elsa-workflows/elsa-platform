using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Manifest;

public sealed record NormalizedManifest(
    EnvironmentManifest Manifest,
    IReadOnlyCollection<DeploymentResource> Resources,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
}
