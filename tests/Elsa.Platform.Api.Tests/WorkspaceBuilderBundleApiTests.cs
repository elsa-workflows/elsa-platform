using System.Net;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Public.Builder;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceBuilderBundleApiTests
{
    [Fact]
    public async Task Workspace_member_can_generate_bundle()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app, "user-123");
        var workspaceId = (await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadPlatformJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().Contain(x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public async Task Workspace_bundle_rejects_anonymous_and_non_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "user-123");
        var workspaceId = (await member.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var anonymous = await app.CreateClient().PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());
        var nonMember = await WorkspaceClient(app, "user-456").PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/builder/bundle", MinimalRequest());

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_bundle_does_not_leak_foreign_private_source_ids()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = WorkspaceClient(app, "owner");
        var ownerWorkspaceId = (await owner.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var sourceId = await AddPrivatePackageAsync(app, ownerWorkspaceId);
        var other = WorkspaceClient(app, "other");
        var otherWorkspaceId = (await other.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var response = await other.PostPlatformJsonAsync($"/api/workspaces/{otherWorkspaceId}/builder/bundle", new BuilderBundleRequest(
            new BuilderBundleImageRequest("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BuilderBundlePackageRequest(sourceId, "Elsa.Private", "1.0.0", [], null)],
            [new BuilderBundlePackageSourceRequest(sourceId, null, null, null)],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadPlatformJsonAsync<BuilderBundleResponse>();
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

    private static async Task<Guid> AddPrivatePackageAsync(PlatformApiTestApplication app, Guid workspaceId)
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
