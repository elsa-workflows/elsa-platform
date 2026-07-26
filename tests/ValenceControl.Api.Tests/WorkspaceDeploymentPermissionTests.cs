using System.Net;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceDeploymentPermissionTests
{
    [Fact]
    public async Task Owner_can_create_application_environment_and_engine()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var application = await CreateApplicationAsync(owner, workspaceId);
        var environment = await CreateEnvironmentAsync(owner, workspaceId, application.Id);
        var engine = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines", EngineRequest());
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        engine.StatusCode.Should().Be(HttpStatusCode.Created);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Operations");
        cockpit.Engines.Should().ContainSingle(x => x.Name == "claims-prod" && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
    }

    [Fact]
    public async Task Owner_can_edit_application_environment_and_engine()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var application = await CreateApplicationAsync(owner, workspaceId);
        var environment = await CreateEnvironmentAsync(owner, workspaceId, application.Id);
        var engineResponse = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines", EngineRequest());
        var engine = (await engineResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>())!;

        var applicationUpdate = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}",
            new WorkspaceDeploymentApplicationRequest("Claims Control", "Updated"));
        var environmentUpdate = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{environment.Id}",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        var engineUpdate = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}",
            EngineRequest() with
            {
                Name = "claims-prod-02",
                BaseUrl = "https://workflows-02.example.test/elsa",
                CredentialReference = "kv://claims/prod/elsa-api-v2"
            });
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        applicationUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        environmentUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        engineUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Control");
        cockpit.Applications.Single().Environments.Should().ContainSingle(x => x.Name == "Production");
        cockpit.Engines.Should().ContainSingle(x =>
            x.Name == "claims-prod-02"
            && x.Endpoint.BaseUrl == "https://workflows-02.example.test/elsa"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api-v2");
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_create_application()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Denied", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_edit_application()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var application = await CreateApplicationAsync(owner, workspaceId);
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PutControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}", new WorkspaceDeploymentApplicationRequest("Denied", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_with_setup_permission_can_create_application()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var accountId = await app.AddWorkspaceMemberAsync(workspaceId, "setup-member", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, accountId, WorkspaceDeploymentPermissions.ManageSetup);

        var response = await app.CreateTrustedWorkspaceClient("setup-member")
            .PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Allowed", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<WorkspaceDeploymentApplication> CreateApplicationAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Claims Operations", null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>())!;
    }

    private static async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(HttpClient client, Guid workspaceId, Guid applicationId)
    {
        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments", new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>())!;
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
