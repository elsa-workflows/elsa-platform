using ValenceControl.RuntimeBuilder.Abstractions;
using System.Text;

namespace ValenceControl.RuntimeBuilder.Core.Builder.Renderers;

public sealed class ReadmeBundleRenderer : IBundleFileRenderer
{
    public int Order => 50;

    public BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Elsa Runtime");
        builder.AppendLine();
        builder.AppendLine($"Runtime image: `{context.RuntimeImage.Image}:{context.ImageTag}`");
        builder.AppendLine($"Host port: `{context.HostPort}`");
        builder.AppendLine();
        builder.AppendLine("## Packages");
        if (context.Packages.Count == 0)
            builder.AppendLine();

        foreach (var package in context.Packages)
            builder.AppendLine($"- `{package.PackageId}` `{package.Version}` from `{package.Source.Name}`");

        builder.AppendLine();
        builder.AppendLine("## Run");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("docker compose up -d");
        builder.AppendLine("```");

        return new BundleFile("README.md", "markdown", "text/markdown", true, builder.ToString());
    }
}
