using System.Net;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Public.Builder;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.Api.Public.Sources;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Testing;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceIsolationTests
{
    [Fact]
    public async Task Workspace_sources_are_visible_only_to_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var context = await CreateWorkspaceSourceWithPackageAsync(app);
        var nonMember = await ProvisionClientAsync(app, "non-member");

        var ownerSources = await context.Owner.GetPlatformJsonAsync<IReadOnlyList<WorkspaceSourceResponse>>($"/api/workspaces/{context.WorkspaceId}/sources");
        var nonMemberSources = await nonMember.GetAsync($"/api/workspaces/{context.WorkspaceId}/sources");

        ownerSources.Should().ContainSingle(x => x.Id == context.SourceId);
        nonMemberSources.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_packages_are_hidden_from_anonymous_and_non_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var context = await CreateWorkspaceSourceWithPackageAsync(app);
        var nonMember = await ProvisionClientAsync(app, "non-member");

        var ownerPackages = await context.Owner.GetPlatformJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/workspaces/{context.WorkspaceId}/packages?sourceIds={context.SourceId}");
        var anonymousPackages = await app.CreateClient().GetPlatformJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/packages?sourceIds={context.SourceId}");
        var nonMemberPackages = await nonMember.GetAsync($"/api/workspaces/{context.WorkspaceId}/packages?sourceIds={context.SourceId}");

        ownerPackages.Should().ContainSingle(x => x.PackageId == "Elsa.Private");
        anonymousPackages.Should().BeEmpty();
        nonMemberPackages.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_builder_catalog_is_visible_only_to_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var context = await CreateWorkspaceSourceWithPackageAsync(app);
        var nonMember = await ProvisionClientAsync(app, "non-member");

        var ownerCatalog = await context.Owner.GetPlatformJsonAsync<BuilderCatalogResponse>($"/api/workspaces/{context.WorkspaceId}/builder/catalog?sourceIds={context.SourceId}");
        var nonMemberCatalog = await nonMember.GetAsync($"/api/workspaces/{context.WorkspaceId}/builder/catalog?sourceIds={context.SourceId}");

        ownerCatalog!.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Private");
        nonMemberCatalog.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_runtime_configurations_are_isolated_by_workspace()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreatePlatformIdentityClient(subject: "owner");
        var ownerWorkspaceId = await GetWorkspaceIdAsync(owner);
        var created = await owner.PostPlatformJsonAsync($"/api/workspaces/{ownerWorkspaceId}/runtime-configurations", RuntimeConfigurationRequest());
        var createdConfiguration = await created.Content.ReadPlatformJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var nonMember = await ProvisionClientAsync(app, "non-member");
        var nonMemberWorkspaceId = await GetWorkspaceIdAsync(nonMember);

        var crossWorkspaceRead = await nonMember.GetAsync($"/api/workspaces/{ownerWorkspaceId}/runtime-configurations/{createdConfiguration!.Id}");
        var ownWorkspaceList = await nonMember.GetPlatformJsonAsync<IReadOnlyList<WorkspaceRuntimeConfigurationResponse>>($"/api/workspaces/{nonMemberWorkspaceId}/runtime-configurations");

        crossWorkspaceRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ownWorkspaceList.Should().BeEmpty();
    }

    [Fact]
    public async Task Anonymous_public_catalog_does_not_include_workspace_sources()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var publicSource = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(publicSource, "Elsa.Public"));
            db.PackageSources.Add(publicSource);
            return Task.CompletedTask;
        });
        var context = await CreateWorkspaceSourceWithPackageAsync(app);

        var publicSources = await app.CreateClient().GetPlatformJsonAsync<IReadOnlyList<PublicSourceResponse>>("/api/sources");

        publicSources.Should().ContainSingle(x => x.Name == "Test NuGet");
        publicSources.Should().NotContain(x => x.Id == context.SourceId);
    }

    private static async Task<WorkspacePackageContext> CreateWorkspaceSourceWithPackageAsync(PlatformApiTestApplication app)
    {
        var owner = app.CreatePlatformIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        await admin.PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 2, 500, 20, 25, false));
        var created = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", SourceRequest());
        var source = await created.Content.ReadPlatformJsonAsync<WorkspaceSourceResponse>();
        await AddPackageAsync(app, source!.Id, "Elsa.Private");
        return new WorkspacePackageContext(owner, workspaceId, source.Id);
    }

    private static async Task<HttpClient> ProvisionClientAsync(PlatformApiTestApplication app, string subject)
    {
        var client = app.CreatePlatformIdentityClient(subject: subject);
        await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        return client;
    }

    private static async Task<Guid> GetWorkspaceIdAsync(HttpClient client) =>
        (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

    private static WorkspaceSourceRequest SourceRequest() =>
        new("Private Feed", "https://private.example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceVersionDiscoveryPolicy.AllVersions);

    private static WorkspaceRuntimeConfigurationRequest RuntimeConfigurationRequest() =>
        new("Runtime", null, new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages")));

    private static async Task AddPackageAsync(PlatformApiTestApplication app, Guid sourceId, string packageId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var package = new Package
        {
            PackageId = packageId,
            DisplayName = PackageDisplayNamePolicy.DefaultForPackageId(packageId),
            SourceId = sourceId,
            Approved = true,
            Listed = true,
            LatestVersion = "1.0.0"
        };
        PublicCatalogSeedData.AddVersion(package);
        db.Packages.Add(package);
        await db.SaveChangesAsync();
    }

    private sealed record WorkspacePackageContext(HttpClient Owner, Guid WorkspaceId, Guid SourceId);
}
