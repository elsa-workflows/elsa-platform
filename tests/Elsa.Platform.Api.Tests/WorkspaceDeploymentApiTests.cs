using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentApiTests
{
    [Fact]
    public async Task Workspace_member_can_read_persisted_deployment_cockpit()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient();
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var cockpit = await response.Content.ReadPlatformJsonAsync<DeploymentCockpit>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Operations");
        cockpit.Engines.Should().ContainSingle(x =>
            x.Name == "claims-prod"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
        cockpit.ObservabilityBindings.Should().ContainSingle(x => x.Provider == "Azure Monitor");
        cockpit.DriftReport.Should().ContainSingle(x => x.Area == "RuntimeConfiguration");
    }

    [Fact]
    public async Task Deployment_cockpit_route_rejects_non_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = app.CreateTrustedWorkspaceClient("member");
        var workspaceId = await member.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var nonMember = await app.CreateTrustedWorkspaceClient("other").GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Normal_dataset_cockpit_loads_under_three_seconds()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("large-workspace");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedNormalDatasetAsync(app, workspaceId);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        stopwatch.Stop();
        var cockpit = await response.Content.ReadPlatformJsonAsync<DeploymentCockpit>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        cockpit!.Applications.Should().HaveCount(25);
        cockpit.Engines.Should().HaveCount(200);
    }

    [Fact]
    public async Task Owner_can_create_update_and_read_environment_tier_shape()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-environment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetPlatformJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var production = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Production.ToString());
        var uat = await CreateTierAsync(owner, workspaceId, "UAT", DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget);
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production, production.Id));
        var environment = await environmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
        var updateResponse = await owner.PutPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{environment!.Id}",
            new WorkspaceDeploymentEnvironmentRequest("UAT", EnvironmentTier.Stage, uat.Id));
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        environmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Single().Environments.Should().ContainSingle(x =>
            x.Id == environment.Id.ToString("D")
            && x.TierName == "UAT"
            && x.TierCapabilities != null
            && x.TierCapabilities.Contains(DeploymentTierCapabilities.PreproductionLike));
    }

    [Fact]
    public async Task Environment_assignment_rejects_archived_tiers()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-archive-env-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var uat = await CreateTierAsync(owner, workspaceId, "UAT", DeploymentTierCapabilities.PreproductionLike);
        var archiveResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/tiers/{uat.Id}/archive", null);
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();

        var environmentResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("UAT", EnvironmentTier.Stage, uat.Id));

        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        environmentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Owner_can_create_desired_state_revision_and_preview_promotion()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("preview-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);

        var sourceRevisionResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{sourceEnvironment.Id}/revisions",
            new WorkspaceDesiredStateRevisionRequest(
                "Stage candidate",
                "stage123",
                [
                    Record(DesiredStateRecordKind.Workflow, "Payment Retry", "{\"version\":8}"),
                    Record(DesiredStateRecordKind.SecretReference, "Payment API", "{\"reference\":\"kv://claims/prod/payment-api\"}")
                ]));
        var targetRevision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, targetEnvironment.Id, "Prod baseline", "{\"records\":[{\"kind\":\"Workflow\",\"name\":\"Payment Retry\",\"payload\":{\"version\":7}}]}");
        var sourceRevision = await sourceRevisionResponse.Content.ReadPlatformJsonAsync<WorkspaceDesiredStateRevision>();

        var previewResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions/preview",
            new WorkspacePromotionPreviewRequestDto(sourceEnvironment.Id, targetEnvironment.Id, sourceRevision!.Id, targetEngine.Id));
        var preview = await previewResponse.Content.ReadPlatformJsonAsync<PromotionComparison>();

        sourceRevisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        preview!.SourceRevision.Should().Be(sourceRevision.RevisionNumber);
        preview.TargetRevision.Should().Be(targetRevision.RevisionNumber);
        preview.Diff.Should().Contain(x => x.Name == "Payment Retry" && x.Impact == DiffImpact.Changed);
        preview.Diff.Should().Contain(x => x.Name == "Payment API" && x.Impact == DiffImpact.Added);
        preview.Validations.Should().Contain(x => x.Severity == ValidationSeverity.Pass);
    }

    [Fact]
    public async Task Promotion_preview_requires_preview_permission_for_readers()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("preview-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var sourceRevision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, sourceEnvironment.Id, "Stage candidate", "{\"records\":[]}");
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "preview-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("preview-reader");
        var request = new WorkspacePromotionPreviewRequestDto(sourceEnvironment.Id, targetEnvironment.Id, sourceRevision.Id, targetEngine.Id);

        var denied = await reader.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/promotions/preview", request);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.PreviewPromotion);
        var allowed = await reader.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/promotions/preview", request);

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Owner_can_confirm_queue_inspect_and_rollback_deployment_run()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("run-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, sourceEnvironment.Id, "Stage candidate", "{\"records\":[]}");

        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);
        var runResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, DeploymentRunMode.Apply));
        var run = await runResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentRun>();
        var detail = await owner.GetPlatformJsonAsync<WorkspaceDeploymentRunDetailResponse>($"/api/workspaces/{workspaceId}/deployments/runs/{run!.Id}");

        await CompleteRunAsync(app, workspaceId, run.Id);
        var rollbackConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Rollback, revision.Id);
        var rollbackResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/rollbacks",
            new WorkspaceRollbackRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, rollbackConfirmation.Id, run.Id, DeploymentRunMode.Apply));
        var rollback = await rollbackResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentRun>();

        runResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        detail!.Run.Id.Should().Be(run.Id);
        detail.History.Should().ContainSingle(x => x.Status == WorkspaceDeploymentRunStatus.Queued);
        rollbackResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        rollback!.RollbackSourceRunId.Should().Be(run.Id);
    }

    [Fact]
    public async Task Deployment_run_confirmation_rejects_wrong_user_replay_and_expired_confirmation()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("run-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, sourceEnvironment.Id, "Stage candidate", "{\"records\":[]}");
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "run-reader", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.ExecuteDeployment);
        var reader = app.CreateTrustedWorkspaceClient("run-reader");

        var ownerConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);
        var wrongUser = await reader.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var runResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var run = await runResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentRun>();
        await CompleteRunAsync(app, workspaceId, run!.Id);
        var replay = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var expiredConfirmationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(ConfirmationActionType.Deploy, revision.Id.ToString("D"), 0));
        var expiredConfirmation = await expiredConfirmationResponse.Content.ReadPlatformJsonAsync<ActionConfirmation>();
        var expired = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, expiredConfirmation!.Id, DeploymentRunMode.Apply));

        wrongUser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        runResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        expired.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task SeedDeploymentAsync(PlatformApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));
        await store.CreateRevisionAsync(workspaceId, new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Baseline", "abc123", "{\"records\":[]}", null));

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Guid? correlatedRevisionId = null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ObservabilityBindings (Id, WorkspaceId, EnvironmentId, EngineId, Kind, Provider, Status, Scope, CorrelatedRevisionId, Sample)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"Logs"}, {"Azure Monitor"}, {"Connected"}, {"workspace:/prod"}, {correlatedRevisionId}, {"Imported status"});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DriftReportItems (Id, WorkspaceId, EnvironmentId, EngineId, Area, Desired, Observed, Action, DetectedAt)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"RuntimeConfiguration"}, {"Concurrency 32"}, {"Concurrency 16"}, {"Review"}, {DateTimeOffset.UtcNow.UtcTicks});
            """);
    }

    private static async Task<(WorkspaceDeploymentApplication Application, WorkspaceDeploymentEnvironment SourceEnvironment, WorkspaceDeploymentEnvironment TargetEnvironment, WorkspaceWorkflowEngine TargetEngine)> SeedPreviewTopologyAsync(
        PlatformApiTestApplication app,
        Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var sourceEnvironment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Stage", EnvironmentTier.Stage));
        var targetEnvironment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var targetEngine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                targetEnvironment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));

        return (application, sourceEnvironment, targetEnvironment, targetEngine);
    }

    private static async Task<WorkspaceDesiredStateRevision> CreateRevisionDirectAsync(
        PlatformApiTestApplication app,
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string label,
        string desiredStateJson)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        return await store.CreateRevisionAsync(workspaceId, new CreateDesiredStateRevisionRequest(applicationId, environmentId, label, null, desiredStateJson, null));
    }

    private static async Task<ActionConfirmation> CreateConfirmationAsync(HttpClient client, Guid workspaceId, ConfirmationActionType actionType, Guid targetId)
    {
        var response = await client.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(actionType, targetId.ToString("D"), null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<ActionConfirmation>())!;
    }

    private static async Task<WorkspaceDeploymentTier> CreateTierAsync(HttpClient client, Guid workspaceId, string name, params string[] capabilities)
    {
        var response = await client.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers",
            new WorkspaceDeploymentTierRequest(name, null, 90, capabilities));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<WorkspaceDeploymentTier>())!;
    }

    private static async Task CompleteRunAsync(PlatformApiTestApplication app, Guid workspaceId, Guid runId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        await store.UpdateRunStatusAsync(workspaceId, runId, WorkspaceDeploymentRunStatus.Succeeded, "Deployment run completed.", DateTimeOffset.UtcNow);
    }

    private static WorkspaceDesiredStateRecordRequest Record(DesiredStateRecordKind kind, string name, string payloadJson) =>
        new(kind, name, JsonSerializer.Deserialize<JsonElement>(payloadJson, PlatformApiTestApplication.JsonOptions));

    private static async Task SeedNormalDatasetAsync(PlatformApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        for (var appIndex = 0; appIndex < 25; appIndex++)
        {
            var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Application {appIndex:00}", null, null));
            for (var envIndex = 0; envIndex < 4; envIndex++)
            {
                var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, $"Env {envIndex}", EnvironmentTier.Dev));
                for (var engineIndex = 0; engineIndex < 2; engineIndex++)
                {
                    await store.RegisterEngineAsync(
                        workspaceId,
                        new RegisterWorkflowEngineRequest(
                            environment.Id,
                            $"engine-{appIndex:00}-{envIndex:00}-{engineIndex:00}",
                            $"https://engine-{appIndex}-{envIndex}-{engineIndex}.example.test/elsa",
                            null,
                            "Azure Key Vault",
                            $"kv://workspace/{appIndex}/{envIndex}/{engineIndex}",
                            [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                            [],
                            null));
                }
            }
        }
    }
}
