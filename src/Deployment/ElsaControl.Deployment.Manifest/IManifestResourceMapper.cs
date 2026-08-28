using System.Collections;
using System.Text.Json.Nodes;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Manifest;

public interface IManifestResourceMapper
{
    string SectionName { get; }

    IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context);
}

public sealed class ManifestNormalizationContext
{
    private readonly IReadOnlyList<DeploymentDiagnostic> _initialDiagnostics;
    private readonly List<DeploymentDiagnostic> _addedDiagnostics = [];
    private readonly IReadOnlyList<DeploymentDiagnostic> _diagnosticsView;

    public ManifestNormalizationContext(EnvironmentManifest manifest, IEnumerable<DeploymentDiagnostic> diagnostics)
    {
        Manifest = manifest;
        _initialDiagnostics = diagnostics.ToArray();
        _diagnosticsView = new DiagnosticView(_initialDiagnostics, _addedDiagnostics);
    }

    public EnvironmentManifest Manifest { get; }

    public IReadOnlyList<DeploymentDiagnostic> Diagnostics => _diagnosticsView;

    /// <summary>
    /// Adds a mapper diagnostic during the active <see cref="IManifestResourceMapper.Map"/> call.
    /// Diagnostics added after the mapper returns are visible on this captured context only and are not merged into the normalized manifest.
    /// </summary>
    public void AddDiagnostic(DeploymentDiagnostic diagnostic) => _addedDiagnostics.Add(diagnostic);

    internal IReadOnlyCollection<DeploymentDiagnostic> AddedDiagnostics => _addedDiagnostics;

    private sealed class DiagnosticView(
        IReadOnlyList<DeploymentDiagnostic> initialDiagnostics,
        IReadOnlyList<DeploymentDiagnostic> addedDiagnostics) : IReadOnlyList<DeploymentDiagnostic>
    {
        public int Count => initialDiagnostics.Count + addedDiagnostics.Count;

        public DeploymentDiagnostic this[int index] =>
            index < initialDiagnostics.Count ? initialDiagnostics[index] : addedDiagnostics[index - initialDiagnostics.Count];

        public IEnumerator<DeploymentDiagnostic> GetEnumerator() => initialDiagnostics.Concat(addedDiagnostics).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
