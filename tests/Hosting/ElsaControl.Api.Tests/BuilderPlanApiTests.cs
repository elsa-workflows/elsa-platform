using System.Diagnostics;
using System.Net;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.Public.Builder;
using ElsaControl.Api.Workspace;
using ElsaControl.PackageCatalog.Abstractions.Compatibility;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;
using ElsaControl.RuntimeBuilder.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElsaControl.Api.Tests;

public sealed class BuilderPlanApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public BuilderPlanApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Public_plan_returns_resolved_state_and_auto_added_shape()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderPlanApiResponse>();
        Assert.Equal("elsa-pro-combined", body!.Resolved.Image.Slug);
        Assert.Empty(body.AutoAdded.Packages);
        Assert.Empty(body.AutoAdded.Features);
        Assert.Empty(body.AutoAdded.Infrastructure);
    }

    [Fact]
    public async Task Public_plan_timeout_returns_problem_details_with_phase_and_code()
    {
        await using var app = new ControlApiTestApplication(
            new Dictionary<string, string?> { ["RuntimeBuilder:PlanTimeoutSeconds"] = "1" },
            services =>
            {
                services.RemoveAll<IPackageCompatibilityService>();
                services.AddScoped<IPackageCompatibilityService, SlowCompatibilityService>();
            });
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadControlJsonAsync<ProblemDetails>();
        Assert.Equal("Builder planning timed out", problem!.Title);
        Assert.Equal("builder.plan.timeout", problem.Extensions["code"]?.ToString());
        Assert.Equal("compatibility", problem.Extensions["phase"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem.Extensions["traceId"]?.ToString()));
    }

    [Fact]
    public async Task Unexpected_public_plan_failure_returns_diagnosable_problem_details()
    {
        await using var app = new ControlApiTestApplication(configureServices: services =>
        {
            services.RemoveAll<IPackageCompatibilityService>();
            services.AddScoped<IPackageCompatibilityService, ThrowingCompatibilityService>();
        });
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadControlJsonAsync<ProblemDetails>();
        Assert.Equal("Builder planning failed", problem!.Title);
        Assert.Equal("builder.plan.failed", problem.Extensions["code"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem.Extensions["traceId"]?.ToString()));
    }

    [Fact]
    public async Task Public_plan_completes_a_fifteen_package_sixty_seven_feature_intent_under_ten_seconds()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            for (var packageIndex = 0; packageIndex < 15; packageIndex++)
                AddPackage(source, packageIndex, packageIndex < 7 ? 5 : 4);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var packages = Enumerable.Range(0, 15)
            .Select(packageIndex => new BundlePackageSelection(
                sourceId,
                $"Elsa.Scale{packageIndex}",
                "1.0.0",
                Enumerable.Range(0, packageIndex < 7 ? 5 : 4).Select(featureIndex => $"scale-{packageIndex}-{featureIndex}").ToList(),
                null))
            .ToList();
        var intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            packages,
            [new PackageSourceSelection(sourceId)],
            [],
            new LocalPackagesOptions(false, "packages"));

        var started = Stopwatch.GetTimestamp();
        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(intent));
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Planning took {elapsed.TotalSeconds:F2} seconds.");
        var body = await response.Content.ReadControlJsonAsync<BuilderPlanApiResponse>();
        Assert.Equal(15, body!.Resolved.Packages.Count);
        Assert.Equal(67, body.Resolved.Packages.Sum(package => package.SelectedFeatures?.Count ?? 0));
    }

    [Fact]
    public async Task Workspace_plan_requires_membership()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "member");
        var workspaceId = (await member.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var success = await member.PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));
        var anonymous = await app.CreateClient().PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));
        var nonMember = await WorkspaceClient(app, "other").PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
    }

    private static RuntimeBuilderIntent MinimalIntent() =>
        new(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));

    private static HttpClient WorkspaceClient(WebApplicationFactory<Program> app, string subject)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://elsaworkflows.io");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }

    private static void AddPackage(PackageSource source, int packageIndex, int featureCount)
    {
        var packageId = $"Elsa.Scale{packageIndex}";
        var package = PublicCatalogSeedData.CreatePackage(source, packageId);
        var version = PublicCatalogSeedData.AddVersion(package);
        var features = Enumerable.Range(0, featureCount)
            .Select(featureIndex => $"scale-{packageIndex}-{featureIndex}")
            .ToList();
        foreach (var feature in features)
            PublicCatalogSeedData.AddFeature(version, feature, feature);
        version.ManifestJson = $$"""
        {
          "schemaVersion": "1.0",
          "package": { "id": "{{packageId}}", "version": "1.0.0" },
          "displayName": "{{packageId}}",
          "features": [
            {{string.Join(",", features.Select(feature => $$"""{ "id": "{{feature}}", "typeName": "{{feature}}Feature", "displayName": "{{feature}}", "compatibility": { "runtimeKinds": ["elsa.server"] } }"""))}}
          ]
        }
        """;
    }

    private sealed class SlowCompatibilityService : IPackageCompatibilityService
    {
        public async Task<CompatibilityCheckResult> CheckAsync(CompatibilityCheckRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new CompatibilityCheckResult(true, []);
        }
    }

    private sealed class ThrowingCompatibilityService : IPackageCompatibilityService
    {
        public Task<CompatibilityCheckResult> CheckAsync(CompatibilityCheckRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic compatibility failure.");
    }
}
