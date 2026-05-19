using Elsa.Catalog.Core.Builder;
using Elsa.Catalog.Core.Builder.Renderers;
using Elsa.Catalog.Core.DeploymentTemplates;
using FluentAssertions;

namespace Elsa.Catalog.Core.Tests;

public sealed class DeploymentTemplateTargetTests
{
    [Fact]
    public void Registry_uses_docker_compose_as_default_target()
    {
        var registry = Registry();
        var files = registry.Render(null, Context(), []);

        files.Should().ContainSingle(x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public void Azure_container_apps_renderer_returns_bicep_template()
    {
        var files = new AzureContainerAppsTemplateRenderer().Render(Context(), []);

        files.Should().ContainSingle(x => x.Path == "azure-container-app.bicep");
        files.Single().Contents.Should().Contain("Microsoft.App/containerApps");
    }

    [Fact]
    public void Kubernetes_helm_renderer_returns_chart_values_and_deployment()
    {
        var files = new KubernetesHelmTemplateRenderer().Render(Context(), []);

        files.Select(x => x.Path).Should().BeEquivalentTo("helm/Chart.yaml", "helm/values.yaml", "helm/templates/deployment.yaml");
        files.Single(x => x.Path == "helm/values.yaml").Contents.Should().Contain("elsaworkflows/elsa-pro-combined");
    }

    [Fact]
    public void Unsupported_target_returns_error_finding()
    {
        var findings = new List<BundleFinding>();

        var files = Registry().Render("terraform", Context(), findings);

        files.Should().BeEmpty();
        findings.Should().ContainSingle(x => x.Code == "deploymentTarget.unsupported");
    }

    private static DeploymentTemplateRegistry Registry() =>
        new([new DockerComposeBundleRenderer(), new AzureContainerAppsTemplateRenderer(), new KubernetesHelmTemplateRenderer()]);

    private static BundleGenerationContext Context()
    {
        var image = new RuntimeImageCatalog().Find("elsa-pro-combined")!;
        var intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection(image.Slug, image.DefaultTag, image.HostPort, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));
        return new BundleGenerationContext(intent, image, image.DefaultTag, image.HostPort, [], [], []);
    }
}
