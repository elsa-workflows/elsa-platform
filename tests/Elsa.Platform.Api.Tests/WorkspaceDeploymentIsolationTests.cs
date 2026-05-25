using System.Net;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentIsolationTests
{
    [Fact]
    public async Task Deployment_cockpit_exposes_only_requested_workspace_records()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var workspaceAClient = app.CreateTrustedWorkspaceClient("workspace-a-member");
        var workspaceBClient = app.CreateTrustedWorkspaceClient("workspace-b-member");
        var workspaceAId = await workspaceAClient.GetDefaultWorkspaceIdAsync();
        var workspaceBId = await workspaceBClient.GetDefaultWorkspaceIdAsync();
        await SeedApplicationAsync(app, workspaceAId, "Workspace A App");
        await SeedApplicationAsync(app, workspaceBId, "Workspace B App");

        var workspaceAResponse = await workspaceAClient.GetAsync($"/api/workspaces/{workspaceAId}/deployments/cockpit");
        var workspaceACockpit = await workspaceAResponse.Content.ReadPlatformJsonAsync<DeploymentCockpit>();
        var workspaceBFromA = await workspaceAClient.GetAsync($"/api/workspaces/{workspaceBId}/deployments/cockpit");

        workspaceAResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        workspaceACockpit!.Applications.Should().ContainSingle(x => x.Name == "Workspace A App");
        workspaceACockpit.Applications.Should().NotContain(x => x.Name == "Workspace B App");
        workspaceBFromA.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task SeedApplicationAsync(PlatformApiTestApplication app, Guid workspaceId, string name)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest(name, null, null));
    }
}
