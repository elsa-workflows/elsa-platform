using ElsaControl.RuntimeBuilder.Abstractions;
using System.Text;

namespace ElsaControl.RuntimeBuilder.Core.Builder.Renderers;

public sealed class EnvExampleBundleRenderer : IBundleFileRenderer
{
    public int Order => 40;

    public BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Elsa runtime environment");
        foreach (var envVar in context.RuntimeImage.EnvVars.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var value = envVar.Secret
                ? "<set-secret>"
                : context.Intent.Image.EnvOverrides?.GetValueOrDefault(envVar.Name) ?? envVar.DefaultValue ?? "";
            builder.AppendLine($"{envVar.Name}={value}");
        }

        foreach (var package in context.Packages)
        {
            foreach (var feature in package.Features.Where(x => package.SelectedFeatures.Contains(x.Id, StringComparer.OrdinalIgnoreCase)))
            {
                foreach (var setting in feature.Settings.Where(x => x.EnvironmentVariable is not null).OrderBy(x => x.EnvironmentVariable, StringComparer.OrdinalIgnoreCase))
                {
                    var placeholder = setting.Secret ? "<set-secret>" : "";
                    builder.AppendLine($"{setting.EnvironmentVariable}={placeholder}");
                }
            }
        }

        return new BundleFile(".env.example", "dotenv", "text/plain", true, builder.ToString());
    }
}
