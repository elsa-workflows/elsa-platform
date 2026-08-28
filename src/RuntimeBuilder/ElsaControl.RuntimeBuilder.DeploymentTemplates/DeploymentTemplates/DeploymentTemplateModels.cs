using ElsaControl.RuntimeBuilder.Abstractions;

namespace ElsaControl.RuntimeBuilder.DeploymentTemplates;

public static class DeploymentTemplateTargets
{
    public const string DockerCompose = "docker-compose";
    public const string AzureContainerApps = "azure-container-apps";
    public const string KubernetesHelm = "kubernetes-helm";
}

public interface IDeploymentTemplateRenderer
{
    string Target { get; }
    IReadOnlyList<BundleFile> Render(BundleGenerationContext context, List<BundleFinding> findings);
}
