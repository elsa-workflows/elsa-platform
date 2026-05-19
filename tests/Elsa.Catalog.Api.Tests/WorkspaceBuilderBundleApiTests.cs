using System.Net;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Builder;
using Elsa.Catalog.Api.Workspace;
using Elsa.Catalog.Core.Accounts;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Persistence.EntityFrameworkCore;
using Elsa.Catalog.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Catalog.Api.Tests;

public sealed class WorkspaceBuilderBundleApiTests
{
    [Fact]
    public async Task Workspace_member_can_generate_bundle()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app, "user-123");
        var workspaceId = (await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await client.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().Contain(x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public async Task Workspace_bundle_rejects_anonymous_and_non_members()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "user-123");
        var workspaceId = (await member.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var anonymous = await app.CreateClient().PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());
        var nonMember = await WorkspaceClient(app, "user-456").PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_bundle_does_not_leak_foreign_private_source_ids()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = WorkspaceClient(app, "owner");
        var ownerWorkspaceId = (await owner.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var sourceId = await AddPrivatePackageAsync(app, ownerWorkspaceId);
        var other = WorkspaceClient(app, "other");
        var otherWorkspaceId = (await other.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await other.PostCatalogJsonAsync($"/api/workspaces/{otherWorkspaceId}/builder/bundle", new BuilderBundleRequest(
            new BuilderBundleImageRequest("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BuilderBundlePackageRequest(sourceId, "Elsa.Private", "1.0.0", [], null)],
            [new BuilderBundlePackageSourceRequest(sourceId, null, null, null)],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().BeEmpty();
        body.Findings.Should().Contain(x => x.Code == "package.missing");
        body.Findings.Select(x => x.Message).Should().NotContain(message => message.Contains("https://private.example.test", StringComparison.Ordinal));
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

    private static async Task<Guid> AddPrivatePackageAsync(CatalogApiTestApplication app, Guid workspaceId)
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
