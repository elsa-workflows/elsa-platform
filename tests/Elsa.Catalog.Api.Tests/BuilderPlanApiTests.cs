using System.Net;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Builder;
using Elsa.Catalog.Api.Workspace;
using Elsa.Catalog.Core.Builder;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Elsa.Catalog.Api.Tests;

public sealed class BuilderPlanApiTests
{
    [Fact]
    public async Task Public_plan_returns_resolved_state_and_auto_added_shape()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostCatalogJsonAsync("/api/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderPlanApiResponse>();
        body!.Resolved.Image.Slug.Should().Be("elsa-pro-combined");
        body.AutoAdded.Packages.Should().BeEmpty();
        body.AutoAdded.Features.Should().BeEmpty();
        body.AutoAdded.Infrastructure.Should().BeEmpty();
    }

    [Fact]
    public async Task Workspace_plan_requires_membership()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "member");
        var workspaceId = (await member.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

        var success = await member.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));
        var anonymous = await app.CreateClient().PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));
        var nonMember = await WorkspaceClient(app, "other").PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/builder/plan", new BuilderPlanApiRequest(MinimalIntent()));

        success.StatusCode.Should().Be(HttpStatusCode.OK);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
}
