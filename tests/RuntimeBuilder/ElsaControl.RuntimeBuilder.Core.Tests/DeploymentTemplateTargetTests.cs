using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Core.Builder;
using ElsaControl.RuntimeBuilder.Core.Builder.Renderers;
using ElsaControl.RuntimeBuilder.DeploymentTemplates;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class DeploymentTemplateTargetTests
{
    [Fact]
    public void Registry_uses_docker_compose_as_default_target()
    {
        var registry = Registry();
        var files = registry.Render(null, Context(), []);

        Assert.Single(files, x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public void Azure_container_apps_renderer_returns_bicep_template()
    {
        var files = new AzureContainerAppsTemplateRenderer().Render(Context(), []);

        Assert.Single(files, x => x.Path == "azure-container-app.bicep");
        Assert.Contains("Microsoft.App/containerApps", files.Single().Contents);
    }

    [Fact]
    public void Kubernetes_helm_renderer_returns_chart_values_and_deployment()
    {
        var files = new KubernetesHelmTemplateRenderer().Render(Context(), []);

        Assert.Equivalent(new[] { "helm/Chart.yaml", "helm/values.yaml", "helm/templates/deployment.yaml" }, files.Select(x => x.Path));
        Assert.Contains("elsaworkflows/elsa-pro-combined", files.Single(x => x.Path == "helm/values.yaml").Contents);
    }

    [Fact]
    public void Unsupported_target_returns_error_finding()
    {
        var findings = new List<BundleFinding>();

        var files = Registry().Render("terraform", Context(), findings);

        Assert.Empty(files);
        Assert.Single(findings, x => x.Code == "deploymentTarget.unsupported");
    }

    private static DeploymentTemplateRegistry Registry() =>
        new([new DockerComposeBundleRenderer(), new AzureContainerAppsTemplateRenderer(), new KubernetesHelmTemplateRenderer()]);

    private static BundleGenerationContext Context()
    {
        var image = RuntimeImageFixtures.Catalog().Find("elsa-pro-combined")!;
        var intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection(image.Slug, image.DefaultTag, image.HostPort, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));
        return new BundleGenerationContext(intent, image, image.DefaultTag, image.HostPort, [], [], []);
    }
}
