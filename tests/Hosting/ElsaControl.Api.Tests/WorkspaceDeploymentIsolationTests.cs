using System.Net;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceDeploymentIsolationTests
{
    [Fact]
    public async Task Deployment_cockpit_exposes_only_requested_workspace_records()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var workspaceAClient = app.CreateTrustedWorkspaceClient("workspace-a-member");
        var workspaceBClient = app.CreateTrustedWorkspaceClient("workspace-b-member");
        var workspaceAId = await workspaceAClient.GetDefaultWorkspaceIdAsync();
        var workspaceBId = await workspaceBClient.GetDefaultWorkspaceIdAsync();
        await SeedApplicationAsync(app, workspaceAId, "Workspace A App");
        await SeedApplicationAsync(app, workspaceBId, "Workspace B App");

        var workspaceAResponse = await workspaceAClient.GetAsync($"/api/workspaces/{workspaceAId}/deployments/cockpit");
        var workspaceACockpit = await workspaceAResponse.Content.ReadControlJsonAsync<DeploymentCockpit>();
        var workspaceBFromA = await workspaceAClient.GetAsync($"/api/workspaces/{workspaceBId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.OK, workspaceAResponse.StatusCode);
        Assert.Single(workspaceACockpit!.Applications, x => x.Name == "Workspace A App");
        Assert.DoesNotContain(workspaceACockpit.Applications, x => x.Name == "Workspace B App");
        Assert.Equal(HttpStatusCode.Forbidden, workspaceBFromA.StatusCode);
    }

    private static async Task SeedApplicationAsync(ControlApiTestApplication app, Guid workspaceId, string name)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest(name, null, null));
    }
}
