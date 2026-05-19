namespace Elsa.Platform.PackageCatalog.Core.Builder;

public sealed class RuntimeImageCatalog
{
    private static readonly IReadOnlyList<RuntimeImage> Images =
    [
        new(
            "elsa-pro-server",
            "Elsa Professional Server",
            "Professional Elsa Server runtime.",
            "elsaworkflows/elsa-pro-server",
            ["latest"],
            "latest",
            8080,
            8080,
            "elsa-pro-server",
            "Professional",
            "Stable",
            ["server"],
            [
                new("ASPNETCORE_ENVIRONMENT", "Environment", "ASP.NET Core environment.", false, false, "Development", "Runtime", false)
            ],
            new(true, true, false, false, null),
            new("https://hub.docker.com/", [], false, true)),
        new(
            "elsa-pro-studio",
            "Elsa Professional Studio",
            "Professional Elsa Studio runtime.",
            "elsaworkflows/elsa-pro-studio",
            ["latest"],
            "latest",
            8080,
            8081,
            "elsa-pro-studio",
            "Professional",
            "Stable",
            ["studio"],
            [
                new("Backend__Url", "Backend URL", "URL used by Studio to reach the Elsa Server backend.", false, false, "http://elsa-pro-server:8080", "Runtime", false)
            ],
            new(true, true, true, true, "elsa-pro-server"),
            new("https://hub.docker.com/", [], false, true)),
        new(
            "elsa-pro-combined",
            "Elsa Professional Combined",
            "Combined Elsa Server and Studio runtime for simple deployments.",
            "elsaworkflows/elsa-pro-combined",
            ["latest"],
            "latest",
            8080,
            8080,
            "elsa-pro-combined",
            "Professional",
            "Stable",
            ["server", "studio"],
            [
                new("ASPNETCORE_ENVIRONMENT", "Environment", "ASP.NET Core environment.", false, false, "Development", "Runtime", false),
                new("Backend__Url", "Backend URL", "URL used by Studio to reach the Elsa Server backend.", false, false, "http://localhost:8080", "Runtime", false)
            ],
            new(true, true, false, false, null),
            new("https://hub.docker.com/", [], false, true))
    ];

    public IReadOnlyList<RuntimeImage> ListImages() => Images;

    public RuntimeImage? Find(string slug) =>
        Images.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
