using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Manifest;

public interface IManifestResourceMapper
{
    string SectionName { get; }

    IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context);
}

public sealed record ManifestNormalizationContext(
    EnvironmentManifest Manifest,
    IList<DeploymentDiagnostic> Diagnostics);
