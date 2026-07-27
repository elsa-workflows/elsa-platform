using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.Api.Public.Compatibility;
using ValenceControl.Api.Public.Packages;
using ValenceControl.Api.Public.Sources;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.Api.Workspace;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceCustomFeedsApiTests
{
    [Fact]
    public async Task Me_workspaces_provisions_account_and_personal_workspace_idempotently()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);

        var first = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!;
        var second = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!;

        Assert.NotEqual(Guid.Empty, first.Account.Id);
        Assert.Equal("ada@example.test", first.Account.Email);
        Assert.Single(first.Workspaces, x => x.Role == WorkspaceRole.Owner);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.Equal(first.Workspaces.Single().Id, second.Workspaces.Single().Id);
    }

    [Fact]
    public async Task Me_workspaces_handles_concurrent_first_sign_in_for_same_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => WorkspaceClient(app).GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces")));

        Assert.Equal(0, responses.Count(x => x is null));
        Assert.Single(responses.Select(x => x!.Account.Id).Distinct());
        Assert.Single(responses.Select(x => x!.Workspaces.Single().Id).Distinct());
    }

    [Fact]
    public async Task Me_workspaces_rejects_missing_trusted_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_trusted_headers_from_untrusted_remote_ip()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        client.DefaultRequestHeaders.Add(ControlApiTestApplication.TestRemoteIpHeader, "203.0.113.10");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_source_creation_requires_entitlement_and_enforces_source_limit()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var denied = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var admin = AdminClient(app);
        var entitlement = await admin.PutControlJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));
        Assert.Equal(HttpStatusCode.OK, entitlement.StatusCode);

        var credentialUrl = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json?token=secret"));
        Assert.Equal(HttpStatusCode.BadRequest, credentialUrl.StatusCode);

        var created = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var source = (await created.Content.ReadControlJsonAsync<WorkspaceSourceResponse>())!;
        Assert.Equal(PackageSourceVisibility.Workspace, source.Ownership);
        Assert.Equal("https://nuget.example.test/v3/index.json", source.Url);

        var overLimit = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Second Feed", "https://nuget2.example.test/v3/index.json"));
        Assert.Equal(HttpStatusCode.Forbidden, overLimit.StatusCode);
    }

    [Fact]
    public async Task Admin_entitlement_update_returns_not_found_for_unknown_workspace()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await AdminClient(app).PutControlJsonAsync(
            $"/api/admin/workspaces/{Guid.NewGuid()}/entitlements",
            new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_source_creation_enforces_source_limit_under_concurrent_requests()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        await AdminClient(app).PutControlJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));

        var responses = await Task.WhenAll(
            WorkspaceClient(app).PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("First Feed", "https://one.example.test/v3/index.json")),
            WorkspaceClient(app).PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Second Feed", "https://two.example.test/v3/index.json")));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Admin_entitlement_update_replaces_existing_workspace_snapshot()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var admin = AdminClient(app);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutControlJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false))).StatusCode);
        var replacement = await admin.PutControlJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(false, 3, 750, 10, 5, true));

        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var body = (await replacement.Content.ReadControlJsonAsync<WorkspaceEntitlementResponse>())!;
        Assert.False(body.CanCreateCustomSources);
        Assert.Equal(3, body.MaxSources);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(1, db.WorkspaceEntitlementSnapshots.Count(x => x.WorkspaceId == workspaceId));
    }

    [Fact]
    public async Task Workspace_sources_and_packages_are_visible_only_to_workspace_members()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(db =>
        {
            var publicSource = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(publicSource, "Elsa.Public"));
            db.PackageSources.Add(publicSource);
            return Task.CompletedTask;
        });
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        await AdminClient(app).PutControlJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 2, 500, 20, 25, false));
        var created = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Private Feed", "https://private.example.test/v3/index.json"));
        var source = (await created.Content.ReadControlJsonAsync<WorkspaceSourceResponse>())!;
        await AddPackageAsync(app, source.Id, "Elsa.Private");

        var publicSources = (await app.CreateClient().GetControlJsonAsync<IReadOnlyList<PublicSourceResponse>>("/api/sources"))!;
        Assert.Single(publicSources, x => x.Name == "Test NuGet");
        Assert.DoesNotContain(publicSources, x => x.Id == source.Id);

        var workspaceSources = (await client.GetControlJsonAsync<IReadOnlyList<WorkspaceSourceResponse>>($"/api/workspaces/{workspaceId}/sources"))!;
        Assert.Contains(workspaceSources, x => x.Id == source.Id && x.Ownership == PackageSourceVisibility.Workspace);

        var publicPackages = (await app.CreateClient().GetControlJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/packages?sourceIds={source.Id}"))!;
        Assert.Empty(publicPackages);

        var workspacePackages = (await client.GetControlJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/workspaces/{workspaceId}/packages?sourceIds={source.Id}"))!;
        Assert.Single(workspacePackages, x => x.PackageId == "Elsa.Private");

        var workspaceBuilderCatalog = (await client.GetControlJsonAsync<BuilderCatalogResponse>($"/api/workspaces/{workspaceId}/builder/catalog?sourceIds={source.Id}"))!;
        Assert.Single(workspaceBuilderCatalog.Packages, x => x.PackageId == "Elsa.Private");

        var publicCompatibility = await app.CreateClient().PostControlJsonAsync("/api/compatibility/check", new CompatibilityCheckApiRequest(
            null,
            null,
            [new SelectedPackageVersionApiRequest(source.Id, "Elsa.Private", "1.0.0")],
            []));
        var publicCompatibilityBody = (await publicCompatibility.Content.ReadControlJsonAsync<CompatibilityCheckApiResponse>())!;
        Assert.Single(publicCompatibilityBody.Findings, x => x.Code == "package.missing");

        var workspaceCompatibility = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/compatibility/check", new CompatibilityCheckApiRequest(
            null,
            null,
            [new SelectedPackageVersionApiRequest(source.Id, "Elsa.Private", "1.0.0")],
            []));
        var workspaceCompatibilityBody = (await workspaceCompatibility.Content.ReadControlJsonAsync<CompatibilityCheckApiResponse>())!;
        Assert.True(workspaceCompatibilityBody.Compatible);

        var anonymousDetail = await app.CreateClient().GetAsync($"/api/sources/{source.Id}/packages/Elsa.Private");
        Assert.Equal(HttpStatusCode.NotFound, anonymousDetail.StatusCode);
    }

    [Fact]
    public async Task Workspace_sources_returns_problem_details_when_identity_lacks_workspace_access()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = WorkspaceClient(app);
        var workspaceId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await WorkspaceClient(app, "other-user").GetAsync($"/api/workspaces/{workspaceId}/sources");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = (await response.Content.ReadControlJsonAsync<ProblemDetails>())!;
        Assert.Equal("Access to this workspace is not allowed.", problem.Title);
        Assert.Equal((int)HttpStatusCode.Forbidden, problem.Status);
    }

    private static WorkspaceSourceRequest CreateSourceRequest(string name, string url) =>
        new(name, url, true, ["Elsa.*"], [], PackageSourceVersionDiscoveryPolicy.AllVersions);

    private static HttpClient WorkspaceClient(WebApplicationFactory<Program> app, string subject = "user-123")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://elsaworkflows.io");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, "ada@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, "Ada Lovelace");
        return client;
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        return client;
    }

    private static async Task AddPackageAsync(ControlApiTestApplication app, Guid sourceId, string packageId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var source = await db.PackageSources.FindAsync(sourceId);
        Assert.NotNull(source);
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
}
