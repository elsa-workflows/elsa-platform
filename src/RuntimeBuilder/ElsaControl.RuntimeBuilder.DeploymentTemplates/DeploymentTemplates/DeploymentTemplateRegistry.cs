using ElsaControl.RuntimeBuilder.Abstractions;

namespace ElsaControl.RuntimeBuilder.DeploymentTemplates;

public sealed class DeploymentTemplateRegistry(IEnumerable<IDeploymentTemplateRenderer> renderers)
{
    private readonly IReadOnlyDictionary<string, IDeploymentTemplateRenderer> renderers = renderers.ToDictionary(x => x.Target, StringComparer.OrdinalIgnoreCase);

    public string NormalizeTarget(string? target) =>
        string.IsNullOrWhiteSpace(target) ? DeploymentTemplateTargets.DockerCompose : target.Trim();

    public IReadOnlyList<BundleFile> Render(string? target, BundleGenerationContext context, List<BundleFinding> findings)
    {
        var normalized = NormalizeTarget(target);
        if (!renderers.TryGetValue(normalized, out var renderer))
        {
            findings.Add(BundleFinding.Error("deploymentTarget.unsupported", $"Deployment target {normalized} is not supported.", $"target:{normalized}"));
            return [];
        }

        return renderer.Render(context, findings);
    }
}
