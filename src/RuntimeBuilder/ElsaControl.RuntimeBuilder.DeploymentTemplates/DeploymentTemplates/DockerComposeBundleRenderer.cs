using ElsaControl.RuntimeBuilder.Abstractions;
using System.Text;
using ElsaControl.RuntimeBuilder.DeploymentTemplates;

namespace ElsaControl.RuntimeBuilder.DeploymentTemplates;

public sealed class DockerComposeBundleRenderer : IDeploymentTemplateRenderer
{
    public string Target => DeploymentTemplateTargets.DockerCompose;

    public IReadOnlyList<BundleFile> Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("services:");
        AppendAppService(builder, context);

        foreach (var provider in context.Infrastructure.Where(x => x.Strategy == "compose-sidecar").OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            AppendProvider(builder, provider);

        return [new BundleFile("docker-compose.yml", "yaml", "application/x-yaml", true, builder.ToString())];
    }

    private static void AppendAppService(StringBuilder builder, BundleGenerationContext context)
    {
        builder.AppendLine("  elsa:");
        builder.AppendLine($"    image: {context.RuntimeImage.Image}:{context.ImageTag}");
        builder.AppendLine($"    container_name: {context.RuntimeImage.ContainerName}");
        builder.AppendLine("    ports:");
        builder.AppendLine($"      - \"{context.HostPort}:{context.RuntimeImage.DefaultPort}\"");

        var envVars = context.RuntimeImage.EnvVars
            .Select(x => (x.Name, Value: x.Secret ? "<set-secret>" : context.Intent.Image.EnvOverrides?.GetValueOrDefault(x.Name) ?? x.DefaultValue))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (envVars.Count > 0)
        {
            builder.AppendLine("    environment:");
            foreach (var (name, value) in envVars)
                builder.AppendLine($"      {name}: \"{EscapeYaml(value!)}\"");
        }

        var dependencies = context.Infrastructure
            .Where(x => x.Strategy == "compose-sidecar")
            .Select(ServiceNameFor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (dependencies.Count > 0)
        {
            builder.AppendLine("    depends_on:");
            foreach (var dependency in dependencies)
                builder.AppendLine($"      - {dependency}");
        }
    }

    private static void AppendProvider(StringBuilder builder, ResolvedInfrastructureProvider provider)
    {
        switch (provider.Provider)
        {
            case "postgres":
                builder.AppendLine("  postgres:");
                builder.AppendLine("    image: postgres:16-alpine");
                builder.AppendLine("    environment:");
                builder.AppendLine("      POSTGRES_DB: elsa");
                builder.AppendLine("      POSTGRES_USER: elsa");
                builder.AppendLine("      POSTGRES_PASSWORD: elsa");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"5432:5432\"");
                break;
            case "sqlserver":
                builder.AppendLine("  sqlserver:");
                builder.AppendLine("    image: mcr.microsoft.com/mssql/server:2022-latest");
                builder.AppendLine("    environment:");
                builder.AppendLine("      ACCEPT_EULA: \"Y\"");
                builder.AppendLine("      SA_PASSWORD: \"Change_this_password_123\"");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"1433:1433\"");
                break;
            case "rabbitmq":
                builder.AppendLine("  rabbitmq:");
                builder.AppendLine("    image: rabbitmq:3-management-alpine");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"5672:5672\"");
                builder.AppendLine("      - \"15672:15672\"");
                break;
            case "redis":
                builder.AppendLine("  redis:");
                builder.AppendLine("    image: redis:7-alpine");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"6379:6379\"");
                break;
            case "azurite":
                builder.AppendLine("  azurite:");
                builder.AppendLine("    image: mcr.microsoft.com/azure-storage/azurite");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"10000:10000\"");
                break;
            case "mailpit":
                builder.AppendLine("  mailpit:");
                builder.AppendLine("    image: axllent/mailpit:latest");
                builder.AppendLine("    ports:");
                builder.AppendLine("      - \"1025:1025\"");
                builder.AppendLine("      - \"8025:8025\"");
                break;
        }
    }

    private static string ServiceNameFor(ResolvedInfrastructureProvider provider) =>
        provider.Provider switch
        {
            "postgres" => "postgres",
            "sqlserver" => "sqlserver",
            "rabbitmq" => "rabbitmq",
            "redis" => "redis",
            "azurite" => "azurite",
            "mailpit" => "mailpit",
            _ => provider.Id
        };

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
