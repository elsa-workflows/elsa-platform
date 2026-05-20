using Elsa.Platform.Deployment.Abstractions.Diagnostics;

namespace Elsa.Platform.Deployment.Manifest;

public sealed record ManifestParseResult(
    EnvironmentManifest? Manifest,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics)
{
    public bool Succeeded => Manifest is not null && Diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
}
