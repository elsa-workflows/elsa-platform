using System.Net;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Public.Builder;
using Elsa.Platform.Api.Public.Compatibility;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.Api.Public.Sources;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceCustomFeedsApiTests
{
    [Fact]
    public async Task Me_workspaces_provisions_account_and_personal_workspace_idempotently()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);

        var first = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        first!.Account.Id.Should().NotBeEmpty();
        first.Account.Email.Should().Be("ada@example.test");
        first.Workspaces.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner);
        second!.Account.Id.Should().Be(first.Account.Id);
        second.Workspaces.Single().Id.Should().Be(first.Workspaces.Single().Id);
    }

    [Fact]
    public async Task Me_workspaces_handles_concurrent_first_sign_in_for_same_identity()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => WorkspaceClient(app).GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces")));

        responses.Count(x => x is null).Should().Be(0);
        responses.Select(x => x!.Account.Id).Distinct().Should().ContainSingle();
        responses.Select(x => x!.Workspaces.Single().Id).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Me_workspaces_rejects_missing_trusted_identity()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_rejects_trusted_headers_from_untrusted_remote_ip()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        client.DefaultRequestHeaders.Add(PlatformApiTestApplication.TestRemoteIpHeader, "203.0.113.10");

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Workspace_source_creation_requires_entitlement_and_enforces_source_limit()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var denied = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = AdminClient(app);
        var entitlement = await admin.PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));
        entitlement.StatusCode.Should().Be(HttpStatusCode.OK);

        var credentialUrl = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json?token=secret"));
        credentialUrl.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var created = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Company Feed", "https://nuget.example.test/v3/index.json"));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var source = await created.Content.ReadPlatformJsonAsync<WorkspaceSourceResponse>();
        source!.Ownership.Should().Be(PackageSourceVisibility.Workspace);
        source.Url.Should().Be("https://nuget.example.test/v3/index.json");

        var overLimit = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Second Feed", "https://nuget2.example.test/v3/index.json"));
        overLimit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_entitlement_update_returns_not_found_for_unknown_workspace()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await AdminClient(app).PutPlatformJsonAsync(
            $"/api/admin/workspaces/{Guid.NewGuid()}/entitlements",
            new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Workspace_source_creation_enforces_source_limit_under_concurrent_requests()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        await AdminClient(app).PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false));

        var responses = await Task.WhenAll(
            WorkspaceClient(app).PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("First Feed", "https://one.example.test/v3/index.json")),
            WorkspaceClient(app).PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Second Feed", "https://two.example.test/v3/index.json")));

        responses.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Forbidden).Should().Be(1);
    }

    [Fact]
    public async Task Admin_entitlement_update_replaces_existing_workspace_snapshot()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var admin = AdminClient(app);

        (await admin.PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 1, 500, 20, 25, false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var replacement = await admin.PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(false, 3, 750, 10, 5, true));

        replacement.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await replacement.Content.ReadPlatformJsonAsync<WorkspaceEntitlementResponse>();
        body!.CanCreateCustomSources.Should().BeFalse();
        body.MaxSources.Should().Be(3);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.WorkspaceEntitlementSnapshots.Count(x => x.WorkspaceId == workspaceId).Should().Be(1);
    }

    [Fact]
    public async Task Workspace_sources_and_packages_are_visible_only_to_workspace_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var publicSource = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(publicSource, "Elsa.Public"));
            db.PackageSources.Add(publicSource);
            return Task.CompletedTask;
        });
        var client = WorkspaceClient(app);
        var workspaceId = (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        await AdminClient(app).PutPlatformJsonAsync($"/api/admin/workspaces/{workspaceId}/entitlements", new WorkspaceEntitlementRequest(true, 2, 500, 20, 25, false));
        var created = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/sources", CreateSourceRequest("Private Feed", "https://private.example.test/v3/index.json"));
        var source = await created.Content.ReadPlatformJsonAsync<WorkspaceSourceResponse>();
        await AddPackageAsync(app, source!.Id, "Elsa.Private");

        var publicSources = await app.CreateClient().GetPlatformJsonAsync<IReadOnlyList<PublicSourceResponse>>("/api/sources");
        publicSources.Should().ContainSingle(x => x.Name == "Test NuGet");
        publicSources.Should().NotContain(x => x.Id == source.Id);

        var workspaceSources = await client.GetPlatformJsonAsync<IReadOnlyList<WorkspaceSourceResponse>>($"/api/workspaces/{workspaceId}/sources");
        workspaceSources.Should().Contain(x => x.Id == source.Id && x.Ownership == PackageSourceVisibility.Workspace);

        var publicPackages = await app.CreateClient().GetPlatformJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/packages?sourceIds={source.Id}");
        publicPackages.Should().BeEmpty();

        var workspacePackages = await client.GetPlatformJsonAsync<IReadOnlyList<PublicPackageResponse>>($"/api/workspaces/{workspaceId}/packages?sourceIds={source.Id}");
        workspacePackages.Should().ContainSingle(x => x.PackageId == "Elsa.Private");

        var workspaceBuilderCatalog = await client.GetPlatformJsonAsync<BuilderCatalogResponse>($"/api/workspaces/{workspaceId}/builder/catalog?sourceIds={source.Id}");
        workspaceBuilderCatalog!.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Private");

        var publicCompatibility = await app.CreateClient().PostPlatformJsonAsync("/api/compatibility/check", new CompatibilityCheckApiRequest(
            null,
            null,
            [new SelectedPackageVersionApiRequest(source.Id, "Elsa.Private", "1.0.0")],
            []));
        var publicCompatibilityBody = await publicCompatibility.Content.ReadPlatformJsonAsync<CompatibilityCheckApiResponse>();
        publicCompatibilityBody!.Findings.Should().ContainSingle(x => x.Code == "package.missing");

        var workspaceCompatibility = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/compatibility/check", new CompatibilityCheckApiRequest(
            null,
            null,
            [new SelectedPackageVersionApiRequest(source.Id, "Elsa.Private", "1.0.0")],
            []));
        var workspaceCompatibilityBody = await workspaceCompatibility.Content.ReadPlatformJsonAsync<CompatibilityCheckApiResponse>();
        workspaceCompatibilityBody!.Compatible.Should().BeTrue();

        var anonymousDetail = await app.CreateClient().GetAsync($"/api/sources/{source.Id}/packages/Elsa.Private");
        anonymousDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Workspace_sources_returns_problem_details_when_identity_lacks_workspace_access()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = WorkspaceClient(app);
        var workspaceId = (await owner.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await WorkspaceClient(app, "other-user").GetAsync($"/api/workspaces/{workspaceId}/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadPlatformJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Access to this workspace is not allowed.");
        problem.Status.Should().Be((int)HttpStatusCode.Forbidden);
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

    private static async Task AddPackageAsync(PlatformApiTestApplication app, Guid sourceId, string packageId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var source = await db.PackageSources.FindAsync(sourceId);
        source.Should().NotBeNull();
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
