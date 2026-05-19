using System.Net;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Builder;
using Elsa.Catalog.Api.Workspace;
using Elsa.Catalog.Core.Builder;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Elsa.Catalog.Api.Tests;

public sealed class WorkspaceRuntimeConfigurationApiTests
{
    [Fact]
    public async Task Workspace_member_can_create_list_and_get_configuration()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);

        var create = await client.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", Request("Production"));
        var created = await create.Content.ReadCatalogJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var list = await client.GetCatalogJsonAsync<IReadOnlyList<WorkspaceRuntimeConfigurationResponse>>($"/api/workspaces/{workspaceId}/runtime-configurations");
        var fetched = await client.GetCatalogJsonAsync<WorkspaceRuntimeConfigurationResponse>($"/api/workspaces/{workspaceId}/runtime-configurations/{created!.Id}");

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        created.Name.Should().Be("Production");
        list.Should().ContainSingle(x => x.Id == created.Id);
        fetched!.Intent.Image.Slug.Should().Be("elsa-pro-combined");
    }

    [Fact]
    public async Task Workspace_member_can_update_delete_and_clone_configuration()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);
        var created = await CreateAsync(client, workspaceId, "Production");

        var update = await client.PutCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}", Request("Staging"));
        var updated = await update.Content.ReadCatalogJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var cloneResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/clone", null);
        var clone = await cloneResponse.Content.ReadCatalogJsonAsync<WorkspaceRuntimeConfigurationResponse>();
        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}");
        var getDeleted = await client.GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}");

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Staging");
        cloneResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        clone!.Name.Should().Be("Staging Copy");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Workspace_member_can_create_versions_and_generate_bundle_from_configuration()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);
        var created = await CreateAsync(client, workspaceId, "Production");

        var versionResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/versions", null);
        var version = await versionResponse.Content.ReadCatalogJsonAsync<WorkspaceRuntimeConfigurationVersionResponse>();
        var versions = await client.GetCatalogJsonAsync<IReadOnlyList<WorkspaceRuntimeConfigurationVersionResponse>>($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/versions");
        var bundleResponse = await client.PostAsync($"/api/workspaces/{workspaceId}/runtime-configurations/{created.Id}/bundle", null);
        var bundle = await bundleResponse.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();

        versionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        version!.VersionNumber.Should().Be(1);
        versions.Should().ContainSingle(x => x.Id == version.Id);
        bundleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        bundle!.Files.Should().Contain(x => x.Path == "docker-compose.yml");
    }

    [Fact]
    public async Task Workspace_configuration_routes_reject_non_members()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "member");
        var workspaceId = await GetWorkspaceIdAsync(member);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");
        var nonMember = await WorkspaceClient(app, "other").GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WorkspaceRuntimeConfigurationResponse> CreateAsync(HttpClient client, Guid workspaceId, string name)
    {
        var response = await client.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", Request(name));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadCatalogJsonAsync<WorkspaceRuntimeConfigurationResponse>())!;
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
        (await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

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
