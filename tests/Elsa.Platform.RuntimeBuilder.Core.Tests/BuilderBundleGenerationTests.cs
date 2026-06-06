using System.Diagnostics;
using System.Text.Json;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers;
using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.RuntimeBuilder.DeploymentTemplates;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Testing;
using Elsa.Platform.RuntimeBuilder.Core.Builder;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Platform.RuntimeBuilder.Core.Tests;

public sealed class BuilderBundleGenerationTests
{
    [Fact]
    public async Task Generates_required_files_for_minimal_valid_bundle()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder().Build());

        result.Findings.Should().NotContain(x => x.Level == "error");
        result.Files.Select(x => x.Path).Should().StartWith(BundleFilePolicy.RequiredFilePaths);
        result.Files.Should().Contain(x => x.Path == "Program.Generated.cs" && !x.Required);
    }

    [Fact]
    public async Task Generates_deterministic_byte_equivalent_output()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);
        var intent = new BuilderBundleFixtureBuilder().WithPackage(source, "Elsa.Email", features: ["email"]).Build();

        var first = await service.GenerateAsync(intent);
        var second = await service.GenerateAsync(intent);

        first.Files.Select(x => (x.Path, x.Contents)).Should().Equal(second.Files.Select(x => (x.Path, x.Contents)));
    }

    [Fact]
    public async Task Bundle_generation_uses_runtime_image_metadata()
    {
        var service = CreateService(PublicCatalogSeedData.CreatePackageSource());

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder().WithImage("elsa-pro-studio", hostPort: 8081).Build());

        var compose = result.Files.Single(x => x.Path == "docker-compose.yml").Contents;
        compose.Should().Contain("image: elsaworkflows/elsa-pro-studio:latest");
        compose.Should().Contain("container_name: elsa-pro-studio");
        compose.Should().Contain("\"8081:8080\"");
        compose.Should().Contain("Backend__Url");
    }

    [Fact]
    public async Task Unknown_runtime_image_returns_error_findings_and_no_files()
    {
        var service = CreateService(PublicCatalogSeedData.CreatePackageSource());

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder().WithImage("missing").Build());

        result.Files.Should().BeEmpty();
        result.Findings.Should().ContainSingle(x => x.Code == "runtimeImage.unknown" && x.Level == "error");
    }

    [Fact]
    public async Task Missing_package_returns_error_findings_and_no_files()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder().WithPackage(source, "Elsa.Missing").Build());

        result.Files.Should().BeEmpty();
        result.Findings.Should().Contain(x => x.Code == "package.missing");
    }

    [Fact]
    public async Task Incompatible_runtime_kind_returns_error_findings_and_no_files()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var packageVersion = CreatePackageVersion(source);
        packageVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email",
              "compatibility": { "runtimeKinds": ["elsa.studio"] }
            }
          ]
        }
        """;
        var service = CreateService(new FakePublicCatalogQueries(CreatePackageProjection(source)), [packageVersion]);

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder()
            .WithImage("elsa-pro-server")
            .WithPackage(source, "Elsa.Email", features: ["email"])
            .Build());

        result.Files.Should().BeEmpty();
        result.Findings.Should().Contain(x => x.Code == "feature.runtimeKindUnsupported" && x.Level == "error");
    }

    [Fact]
    public async Task Required_missing_setting_returns_files_with_placeholder_warning()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);

        var result = await service.GenerateAsync(new BuilderBundleFixtureBuilder().WithPackage(source, "Elsa.Email", features: ["email"]).Build());

        result.Files.Should().NotBeEmpty();
        result.Findings.Should().Contain(x => x.Code == "setting.placeholder" && x.Level == "warning");
        result.Files.Single(x => x.Path == "config.json").Contents.Should().Contain("${SMTP_HOST}");
    }

    [Fact]
    public async Task Secret_values_do_not_appear_in_files_or_findings()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source, secretSetting: true);
        using var secret = JsonDocument.Parse("\"super-secret\"");
        var intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [
                new BundlePackageSelection(
                    source.Id,
                    "Elsa.Email",
                    "1.0.0",
                    ["email"],
                    new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>
                    {
                        ["email"] = new Dictionary<string, JsonElement> { ["smtpHost"] = secret.RootElement.Clone() }
                    })
            ],
            [new PackageSourceSelection(source.Id)],
            [],
            new LocalPackagesOptions(false, "packages"));

        var result = await service.GenerateAsync(intent);

        result.Files.Select(x => x.Contents).Should().NotContain(contents => contents.Contains("super-secret", StringComparison.Ordinal));
        result.Findings.Select(x => x.Message).Should().NotContain(message => message.Contains("super-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generation_uses_local_catalog_queries_only()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var queries = new FakePublicCatalogQueries(CreatePackageProjection(source));
        var service = CreateService(queries, [CreatePackageVersion(source)], null);

        await service.GenerateAsync(new BuilderBundleFixtureBuilder().WithPackage(source, "Elsa.Email", features: ["email"]).Build());

        queries.CallCount.Should().BeGreaterThan(0);
        queries.ExternalCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Equivalent_public_and_workspace_intents_generate_equivalent_files()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);
        var intent = new BuilderBundleFixtureBuilder().WithPackage(source, "Elsa.Email", features: ["email"]).Build();

        var publicResult = await service.GenerateAsync(intent);
        var workspaceResult = await service.GenerateAsync(intent, Guid.NewGuid());

        publicResult.Files.Select(x => (x.Path, x.Contents)).Should().Equal(workspaceResult.Files.Select(x => (x.Path, x.Contents)));
    }

    [Fact]
    public async Task Representative_bundle_generation_completes_under_one_second()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);
        var intent = new BuilderBundleFixtureBuilder()
            .WithPackage(source, "Elsa.Email", features: ["email"])
            .WithInfrastructure("database", "postgres-compose")
            .WithInfrastructure("message-broker", "rabbitmq-compose")
            .Build();

        var stopwatch = Stopwatch.StartNew();
        var result = await service.GenerateAsync(intent);
        stopwatch.Stop();

        result.Files.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("minimal-combined.json")]
    [InlineData("postgres-rabbitmq.json")]
    [InlineData("local-packages-custom-source.json")]
    [InlineData("secret-placeholders.json")]
    public async Task Migration_fixtures_satisfy_backend_bundle_contract(string fixtureName)
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var service = CreateService(source);
        var intent = await BuilderBundleFixtureBuilder.LoadIntentAsync(fixtureName);

        var result = await service.GenerateAsync(intent);
        var summary = BuilderBundleFixtureBuilder.Summarize(result);

        result.Findings.Should().NotContain(x => x.Level == "error", summary);
        result.Files.Select(x => x.Path).Should().Contain(BundleFilePolicy.RequiredFilePaths, summary);
    }

    private static BundleGenerationService CreateService(PackageSource source, bool secretSetting = false)
    {
        var packageVersion = CreatePackageVersion(source);
        return CreateService(new FakePublicCatalogQueries(CreatePackageProjection(source, secretSetting)), [packageVersion], null);
    }

    private static BundleGenerationService CreateService(FakePublicCatalogQueries queries, IReadOnlyList<PackageVersion> compatibilityVersions, Guid? workspaceId) =>
        CreateService(queries, compatibilityVersions);

    private static BundleGenerationService CreateService(IPublicCatalogQueries catalog, IReadOnlyList<PackageVersion> compatibilityVersions)
    {
        var compatibility = new CompatibilityCheckService(new FakeCompatibilityQueries(compatibilityVersions), new VersionRangeEvaluator());
        var infrastructure = new InfrastructureProviderCatalog();
        var runtimeImages = new RuntimeImageCatalog();
        return new BundleGenerationService(
            catalog,
            compatibility,
            runtimeImages,
            infrastructure,
            new BuilderPlannerService(catalog, compatibility, runtimeImages, infrastructure),
            new DeploymentTemplateRegistry(
            [
                new DockerComposeBundleRenderer(),
                new AzureContainerAppsTemplateRenderer(),
                new KubernetesHelmTemplateRenderer()
            ]),
            [
                new AppSettingsBundleRenderer(new BundleFindingPolicy()),
                new PackageLockBundleRenderer(),
                new EnvExampleBundleRenderer(),
                new ReadmeBundleRenderer(),
                new ProgramReferenceBundleRenderer()
            ],
            new BundleFindingPolicy(),
            new BundleFilePolicy(),
            NullLogger<BundleGenerationService>.Instance);
    }

    private static PublicPackageProjection CreatePackageProjection(PackageSource source, bool secretSetting = false)
    {
        var sourceProjection = new PublicPackageSourceProjection(source.Id, source.Name, source.Url);
        var setting = new PublicFeatureSettingProjection(
            "smtpHost",
            "System.String",
            "string",
            true,
            null,
            "SMTP host",
            null,
            "Connection",
            "{}",
            secretSetting,
            false,
            "SMTP_HOST",
            "{}",
            "{}");
        var feature = new PublicFeatureProjection(
            "email",
            "Elsa.Email",
            "1.0.0",
            sourceProjection,
            "Elsa.Email.EmailFeature",
            "Email",
            null,
            "Communication",
            ["Communication"],
            [],
            ["elsa.server"],
            [],
            [],
            [],
            false,
            false,
            "{}",
            [setting]);
        var version = new PublicPackageVersionProjection("Elsa.Email", "1.0.0", sourceProjection, "1.0", null, [feature]);
        return new PublicPackageProjection("Elsa.Email", "Email", sourceProjection, "1.0.0", [version]);
    }

    private static PackageVersion CreatePackageVersion(PackageSource source)
    {
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            { "id": "email", "typeName": "Elsa.Email.EmailFeature", "displayName": "Email", "compatibility": { "runtimeKinds": ["elsa.server"] } }
          ]
        }
        """;
        return version;
    }

    private sealed class FakePublicCatalogQueries(PublicPackageProjection package) : IPublicCatalogQueries
    {
        public int CallCount { get; private set; }
        public int ExternalCallCount { get; }

        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<PublicPackageProjection>>(MatchesSource(sourceIds) ? [package] : []);
        }

        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) =>
            ListPackagesAsync(sourceIds, cancellationToken);

        public Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PublicPackageProjection?>(sourceId == package.Source.Id && packageId == package.PackageId ? package : null);

        public Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            GetPackageAsync(sourceId, packageId, cancellationToken);

        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicPackageVersionProjection>>(sourceId == package.Source.Id && packageId == package.PackageId ? package.Versions : []);

        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            ListVersionsAsync(sourceId, packageId, cancellationToken);

        public Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(package.Source.Id == sourceId && package.PackageId == packageId
                ? package.Versions.SingleOrDefault(x => x.Version == version)
                : null);
        }

        public Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
            GetVersionAsync(sourceId, packageId, version, cancellationToken);

        public Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicFeatureProjection>>(package.Versions.SelectMany(x => x.Features).ToList());

        public Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default) =>
            Task.FromResult(package.Versions.SelectMany(x => x.Features).FirstOrDefault(x => x.FeatureId == featureId));

        private bool MatchesSource(IReadOnlyList<Guid> sourceIds) =>
            sourceIds.Count == 0 || sourceIds.Contains(package.Source.Id);
    }

    private sealed class FakeCompatibilityQueries(IReadOnlyList<PackageVersion> versions) : ICompatibilityQueries
    {
        public Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.SingleOrDefault(x => x.Package?.SourceId == sourceId && x.Package.PackageId == packageId && x.Version == version));
    }
}
