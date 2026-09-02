using System.Net;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceDeploymentPermissionTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public WorkspaceDeploymentPermissionTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Owner_can_create_application_environment_and_engine()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var application = await CreateApplicationAsync(owner, workspaceId);
        var environment = await CreateEnvironmentAsync(owner, workspaceId, application.Id);
        var engine = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines", EngineRequest());
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Created, engine.StatusCode);
        Assert.Single(cockpit!.Applications, x => x.Name == "Claims Operations");
        Assert.Single(cockpit.Engines, x => x.Name == "claims-prod" && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
    }

    [Fact]
    public async Task Owner_can_edit_application_environment_and_engine()
    {
        var app = _app;
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

        Assert.Equal(HttpStatusCode.OK, applicationUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, environmentUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, engineUpdate.StatusCode);
        Assert.Single(cockpit!.Applications, x => x.Name == "Claims Control");
        Assert.Single(cockpit.Applications.Single().Environments, x => x.Name == "Production");
        Assert.Single(cockpit.Engines, x =>
            x.Name == "claims-prod-02"
            && x.Endpoint.BaseUrl == "https://workflows-02.example.test/elsa"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api-v2");
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_create_application()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Denied", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Member_without_setup_permission_cannot_edit_application()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var application = await CreateApplicationAsync(owner, workspaceId);
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("reader")
            .PutControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}", new WorkspaceDeploymentApplicationRequest("Denied", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Member_with_setup_permission_can_create_application()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var accountId = await app.AddWorkspaceMemberAsync(workspaceId, "setup-member", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, accountId, WorkspaceDeploymentPermissions.ManageSetup);

        var response = await app.CreateTrustedWorkspaceClient("setup-member")
            .PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Allowed", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<WorkspaceDeploymentApplication> CreateApplicationAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications", new WorkspaceDeploymentApplicationRequest("Claims Operations", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>())!;
    }

    private static async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(HttpClient client, Guid workspaceId, Guid applicationId)
    {
        var response = await client.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments", new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
