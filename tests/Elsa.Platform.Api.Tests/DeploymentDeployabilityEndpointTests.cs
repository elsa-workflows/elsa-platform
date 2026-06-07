using System.Net;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class DeploymentDeployabilityEndpointTests
{
    [Fact]
    public async Task Owner_can_evaluate_revision_deployability_for_compatible_and_blocked_engines()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deployability-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedDeployabilityRevisionAsync(app, owner, workspaceId, "deployability-owner");

        var compatibleResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/revisions/{seeded.Revision.Id}/deployability",
            new WorkspaceDeployabilityRequestDto(seeded.Environment.Id, seeded.CompatibleEngine.Id, DeploymentRunMode.Apply));
        var compatible = await compatibleResponse.Content.ReadPlatformJsonAsync<DeploymentDeployabilityResult>();
        var blockedResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/revisions/{seeded.Revision.Id}/deployability",
            new WorkspaceDeployabilityRequestDto(seeded.Environment.Id, seeded.BlockedEngine.Id, DeploymentRunMode.Apply));
        var blocked = await blockedResponse.Content.ReadPlatformJsonAsync<DeploymentDeployabilityResult>();

        compatibleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        compatible!.Status.Should().Be(DeploymentDeployabilityStatus.Deployable);
        compatible.CanDeploy.Should().BeTrue();
        compatible.Artifacts.Should().ContainSingle(x => x.ArtifactRecordId == seeded.Artifact.Id);
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        blocked!.Status.Should().Be(DeploymentDeployabilityStatus.Blocked);
        blocked.CanDeploy.Should().BeFalse();
        blocked.Blockers.Select(x => x.Id).Should().Contain(["artifact.capability.missing", "engine.capabilities.missing"]);
    }

    private static async Task<SeededDeployabilityRevision> SeedDeployabilityRevisionAsync(
        PlatformApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject)
    {
        var registerResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:deployability"));
        var artifact = (await registerResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifact>())!;

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
                [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
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
                    "artifactTypeId": "{{ArtifactTypeIds.ElsaWorkflowDefinition}}",
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
