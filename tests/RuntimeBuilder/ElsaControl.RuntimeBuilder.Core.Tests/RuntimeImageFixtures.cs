using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Core.Builder;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

/// <summary>
/// Runtime images for tests that need a catalog to plan or generate against. These stand in for the
/// host's configured catalog, which real deployments define under the RuntimeBuilder section.
/// </summary>
internal static class RuntimeImageFixtures
{
    public static RuntimeImage Server { get; } = new(
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
        ["elsa.server"],
        [AspNetCoreEnvironment],
        new(true, true, false, false, null),
        Docs);

    public static RuntimeImage Studio { get; } = new(
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
        ["elsa.studio"],
        [BackendUrl("http://elsa-pro-server:8080")],
        new(true, true, true, true, "elsa-pro-server"),
        Docs);

    public static RuntimeImage Combined { get; } = new(
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
        ["elsa.server", "elsa.studio"],
        [AspNetCoreEnvironment, BackendUrl("http://localhost:8080")],
        new(true, true, false, false, null),
        Docs);

    public static IReadOnlyList<RuntimeImage> All { get; } = [Server, Studio, Combined];

    public static RuntimeImageCatalog Catalog(params RuntimeImage[] images) =>
        RuntimeImageCatalog.Create(images.Length == 0 ? All : images);

    // Expression-bodied so these do not depend on static initializer ordering with the images above.
    private static RuntimeImageEnvironmentVariable AspNetCoreEnvironment =>
        new("ASPNETCORE_ENVIRONMENT", "Environment", "ASP.NET Core environment.", false, false, "Development", "Runtime", false);

    private static RuntimeImageDocs Docs => new("https://hub.docker.com/", [], false, true);

    private static RuntimeImageEnvironmentVariable BackendUrl(string defaultValue) =>
        new("Backend__Url", "Backend URL", "URL used by Studio to reach the Elsa Server backend.", false, false, defaultValue, "Runtime", false);
}
