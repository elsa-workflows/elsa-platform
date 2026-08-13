using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.Api.Workspace;
using ValenceControl.RuntimeBuilder.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceRuntimeConfigurationApiTests
{
    [Fact]
    public async Task Workspace_member_can_create_list_and_get_configuration()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);

        var create = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", Request("Production"));
        var created = await create.Content.ReadControlJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var list = await client.GetControlJsonAsync<IReadOnlyList<WorkspaceRuntimeConfigurationResponse>>($"/api/workspaces/{workspaceId}/runtime-configurations");
        var fetched = await client.GetControlJsonAsync<WorkspaceRuntimeConfigurationResponse>($"/api/workspaces/{workspaceId}/runtime-configurations/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("Production", created.Name);
        Assert.Single(list!, x => x.Id == created.Id);
        Assert.Equal("elsa-pro-combined", fetched!.Intent.Image.Slug);
    }

    [Fact]
    public async Task Workspace_member_can_update_delete_and_clone_configuration()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);
        var created = await CreateAsync(client, workspaceId, "Production");

        var update = await client.PutControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}", Request("Staging"));
        var updated = await update.Content.ReadControlJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var cloneResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/clone", null);
        var clone = await cloneResponse.Content.ReadControlJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}");
        var getDeleted = await client.GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Staging", updated!.Name);
        Assert.Equal(HttpStatusCode.OK, cloneResponse.StatusCode);
        Assert.Equal("Staging Copy", clone!.Name);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getDeleted.StatusCode);
    }

    [Fact]
    public async Task Workspace_member_can_create_versions_and_generate_bundle_from_configuration()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);
        var created = await CreateAsync(client, workspaceId, "Production");

        var versionResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/versions", null);
        var version = await versionResponse.Content.ReadControlJsonAsync<WorkspaceRuntimeConfigurationVersionResponse>();
        var versions = await client.GetControlJsonAsync<IReadOnlyList<WorkspaceRuntimeConfigurationVersionResponse>>($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/versions");
        var bundleResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/bundle", null);
        var bundle = await bundleResponse.Content.ReadControlJsonAsync<BuilderBundleResponse>();

        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);
        Assert.Equal(1, version!.VersionNumber);
        Assert.Single(versions!, x => x.Id == version.Id);
        Assert.Equal(HttpStatusCode.OK, bundleResponse.StatusCode);
        Assert.Contains(bundle!.Files, x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public async Task Workspace_configuration_routes_reject_non_members()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "member");
        var workspaceId = await GetWorkspaceIdAsync(member);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");
        var nonMember = await WorkspaceClient(app, "other").GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
    }

    private static async Task<WorkspaceRuntimeConfigurationResponse> CreateAsync(HttpClient client, Guid workspaceId, string name)
    {
        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", Request(name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceRuntimeConfigurationResponse>())!;
    }

    private static WorkspaceRuntimeConfigurationRequest Request(string name) =>
        new(name, "Test runtime", MinimalIntent());

    private static RuntimeBuilderIntent MinimalIntent() =>
        new(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));

    private static async Task<Guid> GetWorkspaceIdAsync(HttpClient client) =>
        (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

    private static HttpClient WorkspaceClient(WebApplicationFactory<Program> app, string subject = "user-123")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://elsaworkflows.io");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }
}
