using System.Text.Json;
using Elsa.Catalog.Core.Builder;
using Elsa.Catalog.Core.Packages;

namespace Elsa.Catalog.Testing;

public sealed class BuilderBundleFixtureBuilder
{
    private RuntimeImageSelection image = new("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>());
    private readonly List<BundlePackageSelection> packages = [];
    private readonly List<PackageSourceSelection> packageSources = [];
    private readonly List<InfrastructureSelection> infrastructure = [];
    private LocalPackagesOptions localPackages = new(false, "packages");

    public BuilderBundleFixtureBuilder WithImage(string slug, string? tag = "latest", int? hostPort = 8080)
    {
        image = image with { Slug = slug, Tag = tag, HostPort = hostPort };
        return this;
    }

    public BuilderBundleFixtureBuilder WithPackage(PackageSource source, string packageId = "Elsa.Email", string version = "1.0.0", IReadOnlyList<string>? features = null)
    {
        packages.Add(new BundlePackageSelection(source.Id, packageId, version, features ?? [], null));
        packageSources.Add(new PackageSourceSelection(source.Id));
        return this;
    }

    public BuilderBundleFixtureBuilder WithInfrastructure(string kind, string providerId, string strategy = "compose-sidecar")
    {
        infrastructure.Add(new InfrastructureSelection(kind, providerId, strategy, null));
        return this;
    }

    public BuilderBundleFixtureBuilder WithLocalPackages(string directoryPath = "packages")
    {
        localPackages = new LocalPackagesOptions(true, directoryPath);
        return this;
    }

    public RuntimeBuilderIntent Build() => new(image, packages, packageSources, infrastructure, localPackages);

    public static async Task<RuntimeBuilderIntent> LoadIntentAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(Path.Combine("Fixtures", "BuilderBundles", fixtureName));
        var intent = await JsonSerializer.DeserializeAsync<RuntimeBuilderIntent>(stream, JsonOptions, cancellationToken);
        return intent ?? throw new InvalidOperationException($"Fixture {fixtureName} did not contain a runtime builder intent.");
    }

    public static string Summarize(BundleGenerationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Files.Select(file => $"{file.Path}:{file.ContentType}:{file.Required}:{file.Contents.Length}")
                .Concat(result.Findings.Select(finding => $"{finding.Level}:{finding.Code}:{finding.Scope}")));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
