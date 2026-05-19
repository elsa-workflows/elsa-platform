using System.Text;
using Elsa.Catalog.Core.Builder;

namespace Elsa.Catalog.Core.DeploymentTemplates;

public sealed class AzureContainerAppsTemplateRenderer : IDeploymentTemplateRenderer
{
    public string Target => DeploymentTemplateTargets.AzureContainerApps;

    public IReadOnlyList<BundleFile> Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("param location string = resourceGroup().location");
        builder.AppendLine($"param containerImage string = '{context.RuntimeImage.Image}:{context.ImageTag}'");
        builder.AppendLine("resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {");
        builder.AppendLine("  name: 'elsa-runtime-env'");
        builder.AppendLine("  location: location");
        builder.AppendLine("}");
        builder.AppendLine("resource app 'Microsoft.App/containerApps@2024-03-01' = {");
        builder.AppendLine("  name: 'elsa-runtime'");
        builder.AppendLine("  location: location");
        builder.AppendLine("  properties: {");
        builder.AppendLine("    managedEnvironmentId: environment.id");
        builder.AppendLine("    configuration: { ingress: { external: true, targetPort: " + context.RuntimeImage.DefaultPort + " } }");
        builder.AppendLine("    template: { containers: [ { name: 'elsa', image: containerImage } ] }");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        return [new BundleFile("azure-container-app.bicep", "bicep", "text/plain", true, builder.ToString())];
    }
}
