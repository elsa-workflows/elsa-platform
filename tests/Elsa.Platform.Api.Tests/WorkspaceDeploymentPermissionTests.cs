using System.Net;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentPermissionTests
{
    [Fact]
    public async Task Owner_can_create_application_environment_and_engine()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var application = await CreateApplicationAsync(owner, workspaceId);
        var environment = await CreateEnvironmentAsync(owner, workspaceId, application.Id);
        var engine = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines", EngineRequest());
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        engine.StatusCode.Should().Be(HttpStatusCode.Created);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Operations");
        cockpit.Engines.Should().ContainSingle(x => x.Name == "claims-prod" && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
    }

    [Fact]
    public async Task Owner_can_edit_application_environment_and_engine()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var application = await CreateApplicationAsync(owner, workspaceId);
        var environment = await CreateEnvironmentAsync(owner, workspaceId, application.Id);
        var engineResponse = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines", EngineRequest());
        var engine = (await engineResponse.Content.ReadPlatformJsonAsync<WorkspaceWorkflowEngine>())!;

        var applicationUpdate = await owner.PutPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}",
            new WorkspaceDeploymentApplicationRequest("Claims Platform", "Updated"));
        var environmentUpdate = await owner.PutPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{environment.Id}",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        var engineUpdate = await owner.PutPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}",
            EngineRequest() with
            {
                Name = "claims-prod-02",
                BaseUrl = "https://workflows-02.example.test/elsa",
                CredentialReference = "kv://claims/prod/elsa-api-v2"
            });
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        applicationUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        environmentUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        engineUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Platform");
        cockpit.Applications.Single().Environments.Should().ContainSingle(x => x.Name == "Production");
        cockpit.Engines.Should().ContainSingle(x =>
            x.Name == "claims-prod-02"
            && x.Endpoint.BaseUrl == "https://workflows-02.example.test/elsa"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api-v2");
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_create_application()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Denied", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_edit_application()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var application = await CreateApplicationAsync(owner, workspaceId);
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PutPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}", new WorkspaceDeploymentApplicationRequest("Denied", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_with_setup_permission_can_create_application()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var accountId = await app.AddWorkspaceMemberAsync(workspaceId, "setup-member", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, accountId, WorkspaceDeploymentPermissions.ManageSetup);

        var response = await app.CreateTrustedWorkspaceClient("setup-member")
            .PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Allowed", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<WorkspaceDeploymentApplication> CreateApplicationAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Claims Operations", null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>())!;
    }

    private static async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(HttpClient client, Guid workspaceId, Guid applicationId)
    {
        var response = await client.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments", new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>())!;
    }

    private static WorkspaceWorkflowEngineRequest EngineRequest() =>
        new(
            "claims-prod",
            "https://workflows.example.test/elsa",
            "westeurope",
            "Azure Key Vault",
            "kv://claims/prod/elsa-api",
            [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
            [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
            null);

}
