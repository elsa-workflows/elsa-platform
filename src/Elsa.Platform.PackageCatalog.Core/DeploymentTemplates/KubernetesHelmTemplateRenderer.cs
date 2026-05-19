using System.Text;
using Elsa.Platform.PackageCatalog.Core.Builder;

namespace Elsa.Platform.PackageCatalog.Core.DeploymentTemplates;

public sealed class KubernetesHelmTemplateRenderer : IDeploymentTemplateRenderer
{
    public string Target => DeploymentTemplateTargets.KubernetesHelm;

    public IReadOnlyList<BundleFile> Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var chart = """
        apiVersion: v2
        name: elsa-runtime
        version: 0.1.0
        appVersion: "1.0.0"
        """;
        var values = $$"""
        image:
          repository: {{context.RuntimeImage.Image}}
          tag: {{context.ImageTag}}
        service:
          port: {{context.RuntimeImage.DefaultPort}}
        """;
        var deployment = new StringBuilder();
        deployment.AppendLine("apiVersion: apps/v1");
        deployment.AppendLine("kind: Deployment");
        deployment.AppendLine("metadata:");
        deployment.AppendLine("  name: {{ .Release.Name }}-elsa");
        deployment.AppendLine("spec:");
        deployment.AppendLine("  replicas: 1");
        deployment.AppendLine("  selector: { matchLabels: { app: elsa-runtime } }");
        deployment.AppendLine("  template:");
        deployment.AppendLine("    metadata: { labels: { app: elsa-runtime } }");
        deployment.AppendLine("    spec:");
        deployment.AppendLine("      containers:");
        deployment.AppendLine("        - name: elsa");
        deployment.AppendLine("          image: \"{{ .Values.image.repository }}:{{ .Values.image.tag }}\"");
        deployment.AppendLine("          ports:");
        deployment.AppendLine("            - containerPort: {{ .Values.service.port }}");
        return
        [
            new BundleFile("helm/Chart.yaml", "yaml", "application/x-yaml", true, chart + Environment.NewLine),
            new BundleFile("helm/values.yaml", "yaml", "application/x-yaml", true, values + Environment.NewLine),
            new BundleFile("helm/templates/deployment.yaml", "yaml", "application/x-yaml", true, deployment.ToString())
        ];
    }
}
