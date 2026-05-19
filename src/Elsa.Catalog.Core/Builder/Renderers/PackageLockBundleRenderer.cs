using System.Text.Json;

namespace Elsa.Catalog.Core.Builder.Renderers;

public sealed class PackageLockBundleRenderer : IBundleFileRenderer
{
    public int Order => 20;

    public BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var document = new
        {
            Version = 1,
            GeneratedBy = "Elsa Platform",
            Image = new
            {
                context.RuntimeImage.Slug,
                Tag = context.ImageTag
            },
            PackageSources = context.PackageSources.Select(source => new
            {
                source.SourceId,
                source.Name,
                source.Url,
                source.Kind
            }),
            Packages = context.Packages.Select(package => new
            {
                package.SourceId,
                package.PackageId,
                package.Version,
                Features = package.SelectedFeatures
            })
        };

        return new BundleFile(
            "packages.lock.json",
            "json",
            "application/json",
            true,
            JsonSerializer.Serialize(document, JsonOptions()) + Environment.NewLine);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
