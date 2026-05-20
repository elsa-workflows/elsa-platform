using System.Collections;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Manifest;

public interface IManifestResourceMapper
{
    string SectionName { get; }

    IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context);
}

public sealed class ManifestNormalizationContext
{
    private readonly IList<DeploymentDiagnostic> _diagnostics;
    private readonly IReadOnlyList<DeploymentDiagnostic> _diagnosticsView;

    public ManifestNormalizationContext(EnvironmentManifest manifest, IList<DeploymentDiagnostic> diagnostics)
    {
        Manifest = manifest;
        _diagnostics = diagnostics;
        _diagnosticsView = new DiagnosticView(diagnostics);
    }

    public EnvironmentManifest Manifest { get; }

    public IReadOnlyList<DeploymentDiagnostic> Diagnostics => _diagnosticsView;

    public void AddDiagnostic(DeploymentDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

    private sealed class DiagnosticView(IList<DeploymentDiagnostic> diagnostics) : IReadOnlyList<DeploymentDiagnostic>
    {
        public int Count => diagnostics.Count;

        public DeploymentDiagnostic this[int index] => diagnostics[index];

        public IEnumerator<DeploymentDiagnostic> GetEnumerator() => diagnostics.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
