using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.Api.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceBuilderBundleApiTests
{
    [Fact]
    public async Task Workspace_member_can_generate_bundle()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app, "user-123");
        var workspaceId = (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        Assert.Contains(body!.Files, x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public async Task Workspace_bundle_rejects_anonymous_and_non_members()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "user-123");
        var workspaceId = (await member.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var anonymous = await app.CreateClient().PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());
        var nonMember = await WorkspaceClient(app, "user-456").PostControlJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
    }

    [Fact]
    public async Task Workspace_bundle_does_not_leak_foreign_private_source_ids()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = WorkspaceClient(app, "owner");
        var ownerWorkspaceId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var sourceId = await AddPrivatePackageAsync(app, ownerWorkspaceId);
        var other = WorkspaceClient(app, "other");
        var otherWorkspaceId = (await other.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await other.PostControlJsonAsync($"/api/workspaces/{otherWorkspaceId}/builder/bundle", new BuilderBundleRequest(
            new BuilderBundleImageRequest("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BuilderBundlePackageRequest(sourceId, "Elsa.Private", "1.0.0", [], null)],
            [new BuilderBundlePackageSourceRequest(sourceId, null, null, null)],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        Assert.Empty(body!.Files);
        Assert.Contains(body.Findings, x => x.Code == "package.missing");
        Assert.DoesNotContain(body.Findings.Select(x => x.Message), message => message.Contains("https://private.example.test", StringComparison.Ordinal));
    }

    private static BuilderBundleRequest MinimalRequest() =>
        new(
            new BuilderBundleImageRequest("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages"));

    private static HttpClient WorkspaceClient(WebApplicationFactory<Program> app, string subject)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://elsaworkflows.io");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }

    private static async Task<Guid> AddPrivatePackageAsync(ControlApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.Name = "Private Feed";
        source.Url = "https://private.example.test/v3/index.json";
        source.Visibility = PackageSourceVisibility.Workspace;
        source.OwnerWorkspaceId = workspaceId;
        PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(source, "Elsa.Private"));
        db.PackageSources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }
}
