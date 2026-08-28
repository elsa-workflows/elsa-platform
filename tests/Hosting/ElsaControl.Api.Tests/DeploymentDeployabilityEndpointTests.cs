using System.Net;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Artifacts;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class DeploymentDeployabilityEndpointTests
{
    [Fact]
    public async Task Owner_can_evaluate_revision_deployability_for_compatible_and_blocked_engines()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployability-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedDeployabilityRevisionAsync(app, owner, workspaceId, "deployability-owner");

        var compatibleResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/revisions/{seeded.Revision.Id}/deployability",
            new WorkspaceDeployabilityRequestDto(seeded.Environment.Id, seeded.CompatibleEngine.Id, DeploymentRunMode.Apply));
        var compatible = await compatibleResponse.Content.ReadControlJsonAsync<DeploymentDeployabilityResult>();
        var blockedResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/revisions/{seeded.Revision.Id}/deployability",
            new WorkspaceDeployabilityRequestDto(seeded.Environment.Id, seeded.BlockedEngine.Id, DeploymentRunMode.Apply));
        var blocked = await blockedResponse.Content.ReadControlJsonAsync<DeploymentDeployabilityResult>();

        Assert.Equal(HttpStatusCode.OK, compatibleResponse.StatusCode);
        Assert.Equal(DeploymentDeployabilityStatus.Deployable, compatible!.Status);
        Assert.True(compatible.CanDeploy);
        Assert.Single(compatible.Artifacts, x => x.ArtifactRecordId == seeded.Artifact.Id);
        Assert.Equal(HttpStatusCode.OK, blockedResponse.StatusCode);
        Assert.Equal(DeploymentDeployabilityStatus.Blocked, blocked!.Status);
        Assert.False(blocked.CanDeploy);
        var blockerIds = blocked.Blockers.Select(x => x.Id);
        Assert.Contains("artifact.capability.missing", blockerIds);
        Assert.Contains("engine.capabilities.missing", blockerIds);
    }

    private static async Task<SeededDeployabilityRevision> SeedDeployabilityRevisionAsync(
        ControlApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject)
    {
        var registerResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:deployability"));
        var artifact = (await registerResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var accountId = await db.ExternalIdentities
            .Where(x => x.Issuer == WorkspaceDeploymentTestFixtures.DefaultIssuer && x.Subject == subject)
            .Select(x => x.AccountId)
            .SingleAsync();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Claims {Guid.NewGuid():N}", null, accountId));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Dev", EnvironmentTier.Dev));
        var compatibleEngine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "dev-01",
                "https://runtime.example.test",
                "westeurope",
                "External secret store",
                "kv://runtime/dev",
                [new EngineCapability("loom.recipe.apply", "Apply Loom recipes", CapabilityBoundary.EngineApi)],
                [],
                "container-apps"));
        var blockedEngine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "dev-02",
                "https://runtime-2.example.test",
                "westeurope",
                "External secret store",
                "kv://runtime/dev-2",
                [],
                [],
                "container-apps"));
        var desiredStateJson = $$"""
            {
              "records": [
                {
                  "kind": "ArtifactReference",
                  "name": "Claims",
                  "payload": {
                    "artifactRecordId": "{{artifact.Id:D}}",
                    "artifactId": "{{artifact.ArtifactId}}",
                    "artifactTypeId": "{{ArtifactTypeIds.ElsaLoomRecipe}}",
                    "contentDigest": {
                      "algorithm": "{{artifact.ContentDigest.Algorithm}}",
                      "value": "{{artifact.ContentDigest.Value}}"
                    }
                  }
                }
              ]
            }
            """;
        var revision = await store.CreateRevisionAsync(
            workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "r1", "abc123", desiredStateJson, accountId));
        return new SeededDeployabilityRevision(environment, compatibleEngine, blockedEngine, revision, artifact);
    }

    private sealed record SeededDeployabilityRevision(
        WorkspaceDeploymentEnvironment Environment,
        WorkspaceWorkflowEngine CompatibleEngine,
        WorkspaceWorkflowEngine BlockedEngine,
        WorkspaceDesiredStateRevision Revision,
        WorkspaceArtifact Artifact);
}
