using System.Net;
using System.Text.Json;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.Public.Builder;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Core.Builder.Renderers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class PublicBuilderBundleApiTests
{
    [Fact]
    public async Task Trusted_builder_client_can_generate_bundle()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var response = await BuilderClient(app).PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        var paths = body!.Files.Select(x => x.Path);
        Assert.Contains("config.json", paths);
        Assert.Contains("packages.lock.json", paths);
        Assert.Contains("docker-compose.yml", paths);
        Assert.Contains(".env.example", paths);
        Assert.Contains("README.md", paths);
        Assert.DoesNotContain(body.Findings, x => x.Level == "error");
    }

    [Fact]
    public async Task Dependency_resolved_plan_can_be_posted_directly_to_bundle()
    {
        await using var app = new ControlApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            AddPackage(source, "Elsa.Agents.Activities", "Elsa.Agents.Activities.AgentsActivities",
                """[{ "packageId": "Elsa.Agents.Core", "featureId": "Elsa.Agents.Core.AgentsCore" }]""");
            AddPackage(source, "Elsa.Agents.Core", "Elsa.Agents.Core.AgentsCore");
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = BuilderClient(app);
        var intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BundlePackageSelection(sourceId, "Elsa.Agents.Activities", "1.0.0", ["Elsa.Agents.Activities.AgentsActivities"], null)],
            [new PackageSourceSelection(sourceId)],
            [],
            new LocalPackagesOptions(false, "packages"));

        var planResponse = await client.PostControlJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(intent));
        var plan = await planResponse.Content.ReadControlJsonAsync<BuilderPlanApiResponse>();
        var bundleResponse = await client.PostControlJsonAsync("/api/builder/bundle", plan!.Resolved);

        Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
        Assert.Single(plan.AutoAdded.Packages, x => x.PackageId == "Elsa.Agents.Core");
        Assert.Single(plan.Resolved.PackageSources);
        Assert.Equal(HttpStatusCode.OK, bundleResponse.StatusCode);
        var bundle = await bundleResponse.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        Assert.True(bundle!.Files.Any(x => x.Path == "packages.lock.json"),
            string.Join(Environment.NewLine, bundle.Findings.Select(x => $"{x.Level}:{x.Code}:{x.Message}")));
        var lockFile = bundle!.Files.Single(x => x.Path == "packages.lock.json");
        using var packageLock = JsonDocument.Parse(lockFile.Contents);
        Assert.Equal(1, packageLock.RootElement.GetProperty("packageSources").GetArrayLength());
        Assert.Equal(2, packageLock.RootElement.GetProperty("packages").GetArrayLength());
        Assert.DoesNotContain(bundle.Findings, x => x.Level == "error");
    }

    [Fact]
    public async Task Unexpected_bundle_failure_returns_diagnosable_problem_details()
    {
        await using var app = new ControlApiTestApplication(configureServices: services =>
            services.AddScoped<IBundleFileRenderer, ThrowingBundleFileRenderer>());
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadControlJsonAsync<ProblemDetails>();
        Assert.Equal("Bundle generation failed", problem!.Title);
        Assert.True(problem.Extensions.TryGetValue("code", out var code));
        Assert.Equal("builder.bundle.failed", code?.ToString());
        Assert.True(problem.Extensions.TryGetValue("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId?.ToString()));
    }

    [Fact]
    public async Task Direct_untrusted_bundle_calls_are_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Builder_credentials_do_not_authorize_admin_apis()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = BuilderClient(app);

        var bundle = await client.PostControlJsonAsync("/api/builder/bundle", MinimalRequest());
        var admin = await client.GetAsync("/api/admin/application");

        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Fact]
    public async Task Admin_credentials_do_not_authorize_builder_bundle_generation()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Blocked_domain_response_returns_empty_files()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostControlJsonAsync("/api/builder/bundle", MinimalRequest(imageSlug: "missing"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        Assert.Empty(body!.Files);
        Assert.Contains(body.Findings, x => x.Level == "error" && x.Code == "runtimeImage.unknown");
    }

    private static BuilderBundleRequest MinimalRequest(string imageSlug = "elsa-pro-combined") =>
        new(
            new BuilderBundleImageRequest(imageSlug, "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages"));

    private static HttpClient BuilderClient(ControlApiTestApplication app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "builder-dev-key");
        return client;
    }

    private static void AddPackage(PackageSource source, string packageId, string featureId, string dependenciesJson = "[]")
    {
        var package = PublicCatalogSeedData.CreatePackage(source, packageId);
        var version = PublicCatalogSeedData.AddVersion(package);
        PublicCatalogSeedData.AddFeature(version, featureId, featureId, dependenciesJson);
        version.ManifestJson = $$"""
        {
          "schemaVersion": "1.0",
          "package": { "id": "{{packageId}}", "version": "1.0.0" },
          "displayName": "{{packageId}}",
          "features": [
            {
              "id": "{{featureId}}",
              "typeName": "{{featureId}}Feature",
              "displayName": "{{featureId}}",
              "compatibility": { "runtimeKinds": ["elsa.server"] },
              "dependencies": {{dependenciesJson}}
            }
          ]
        }
        """;
    }

    private sealed class ThrowingBundleFileRenderer : IBundleFileRenderer
    {
        public int Order => 0;

        public BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings) =>
            throw new InvalidOperationException("Synthetic renderer failure.");
    }
}
