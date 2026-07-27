using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.Api.Workspace;
using ValenceControl.RuntimeBuilder.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ValenceControl.Api.Tests;

public sealed class BuilderPlanApiTests
{
    [Fact]
    public async Task Public_plan_returns_resolved_state_and_auto_added_shape()
    {
        await using var app = new ControlApiTestApplication();
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
    public async Task Workspace_plan_requires_membership()
    {
        await using var app = new ControlApiTestApplication();
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
}
