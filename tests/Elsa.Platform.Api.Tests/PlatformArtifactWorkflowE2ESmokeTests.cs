using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Studio.Submit;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class PlatformArtifactWorkflowE2ESmokeTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();

    [Fact]
    public async Task Studio_submitted_workflow_artifact_can_deploy_apply_and_roll_back()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-e2e-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var topology = await SeedTopologyAsync(app, workspaceId);
        var payloads = new InMemoryPayloadFetcher();
        var runtimeStore = new InMemoryWorkflowDefinitionRuntimeStore();
        var applyJournal = new InMemoryWorkflowArtifactApplyJournal();

        var firstArtifact = await SubmitWorkflowAsync(owner, workspaceId, "1", """{"id":"payment-retry","name":"Payment Retry","version":1}""", payloads);
        var firstDeploy = await PromoteDeployAndApplyAsync(
            app,
            owner,
            workspaceId,
            topology,
            firstArtifact.Artifact,
            "Stage artifact v1",
            "Promote artifact v1",
            payloads,
            runtimeStore,
            applyJournal);

        var secondArtifact = await SubmitWorkflowAsync(owner, workspaceId, "2", """{"id":"payment-retry","name":"Payment Retry","version":2}""", payloads);
        var secondDeploy = await PromoteDeployAndApplyAsync(
            app,
            owner,
            workspaceId,
            topology,
            secondArtifact.Artifact,
            "Stage artifact v2",
            "Promote artifact v2",
            payloads,
            runtimeStore,
            applyJournal);

        var rollbackConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Rollback, firstDeploy.TargetRevision.Id);
        var rollbackResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/rollbacks",
            new WorkspaceRollbackRunRequestDto(
                firstDeploy.TargetRevision.Id,
                topology.TargetEnvironment.Id,
                topology.TargetEngine.Id,
                rollbackConfirmation.Id,
                secondDeploy.Run.Id,
                DeploymentRunMode.Apply));
        rollbackResponse.StatusCode.Should().Be(HttpStatusCode.Created, await rollbackResponse.Content.ReadAsStringAsync());
        var rollback = (await rollbackResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentRun>())!;
        var rollbackApply = await ProcessNextRuntimeCommandAsync(owner, workspaceId, topology.TargetEngine.Id, payloads, runtimeStore, applyJournal);
        var rollbackDetail = await owner.GetPlatformJsonAsync<WorkspaceDeploymentRunDetailResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runs/{rollback.Id}");

        firstArtifact.Submit.Status.Should().Be(StudioSubmitStatus.Submitted);
        firstArtifact.Duplicate.Status.Should().Be(StudioSubmitStatus.Duplicate);
        firstDeploy.Apply.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Completed);
        secondDeploy.Apply.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Completed);
        rollback.RollbackSourceRunId.Should().Be(secondDeploy.Run.Id);
        rollbackApply.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Completed);
        rollbackDetail!.Run.Status.Should().Be(WorkspaceDeploymentRunStatus.RolledBack);
        rollbackDetail.Commands.Should().ContainSingle(x =>
            x.Action == DeploymentCommandAction.Rollback
            && x.Artifact!.ArtifactRecordId == firstArtifact.Artifact.Id
            && x.RuntimeReference == "elsa://workflows/payment-retry");
        runtimeStore.Definitions.Should().ContainSingle(x =>
            x.WorkflowDefinitionId == "payment-retry"
            && x.WorkflowDefinitionJson.Contains("\"version\":1", StringComparison.Ordinal));
    }

    private async Task<SubmittedArtifact> SubmitWorkflowAsync(
        HttpClient owner,
        Guid workspaceId,
        string version,
        string workflowJson,
        InMemoryPayloadFetcher payloads)
    {
        var options = new StudioSubmitOptions
        {
            PlatformEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            ProducerVersion = "4.0.0",
            RuntimeVersionRange = ">=4.0.0"
        };
        var snapshot = new WorkflowSubmissionSnapshot(
            "payment-retry",
            $"v{version}",
            "Payment Retry",
            version,
            "Retries payment collection failures.",
            workflowJson,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            "studio://workflows/payment-retry",
            new Dictionary<string, string> { ["domain"] = "payments" },
            new Dictionary<string, string> { ["owner"] = "finance-ops" });
        var package = _packager.Package(snapshot, options);
        payloads.Add(package.Envelope.PayloadReference.Uri, package.WorkflowDefinitionJson);

        var submitClient = new StudioPlatformArtifactSubmitClient(owner);
        var submit = await submitClient.SubmitAsync(package, options);
        var duplicate = await submitClient.SubmitAsync(package, options);
        var artifacts = await owner.GetPlatformJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts");
        var artifact = artifacts!.Items.Single(x => x.ArtifactId == package.Envelope.ArtifactId);

        return new SubmittedArtifact(artifact, submit, duplicate);
    }

    private static async Task<DeploymentApply> PromoteDeployAndApplyAsync(
        PlatformApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        DeploymentTopology topology,
        WorkspaceArtifact artifact,
        string revisionLabel,
        string promotionLabel,
        InMemoryPayloadFetcher payloads,
        InMemoryWorkflowDefinitionRuntimeStore runtimeStore,
        InMemoryWorkflowArtifactApplyJournal applyJournal)
    {
        var sourceRevisionResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{topology.Application.Id}/environments/{topology.SourceEnvironment.Id}/revisions",
            new WorkspaceDesiredStateRevisionRequest(
                revisionLabel,
                null,
                [
                    ArtifactReferenceRecord(artifact),
                    Record(DesiredStateRecordKind.ObservabilityBinding, "OpenTelemetry", "{\"provider\":\"otlp\"}")
                ]));
        sourceRevisionResponse.StatusCode.Should().Be(HttpStatusCode.Created, await sourceRevisionResponse.Content.ReadAsStringAsync());
        var sourceRevision = (await sourceRevisionResponse.Content.ReadPlatformJsonAsync<WorkspaceDesiredStateRevision>())!;

        var previewResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions/preview",
            new WorkspacePromotionPreviewRequestDto(
                topology.SourceEnvironment.Id,
                topology.TargetEnvironment.Id,
                sourceRevision.Id,
                topology.TargetEngine.Id));
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());

        var promotionResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions",
            new WorkspacePromotionRequestDto(
                topology.SourceEnvironment.Id,
                topology.TargetEnvironment.Id,
                sourceRevision.Id,
                topology.TargetEngine.Id,
                promotionLabel,
                null));
        promotionResponse.StatusCode.Should().Be(HttpStatusCode.Created, await promotionResponse.Content.ReadAsStringAsync());
        var promotion = (await promotionResponse.Content.ReadPlatformJsonAsync<WorkspacePromotionResult>())!;
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, promotion.TargetRevision.Id);
        var runResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(
                promotion.TargetRevision.Id,
                topology.TargetEnvironment.Id,
                topology.TargetEngine.Id,
                confirmation.Id,
                DeploymentRunMode.Apply));
        runResponse.StatusCode.Should().Be(HttpStatusCode.Created, await runResponse.Content.ReadAsStringAsync());
        var run = (await runResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentRun>())!;
        var apply = await ProcessNextRuntimeCommandAsync(owner, workspaceId, topology.TargetEngine.Id, payloads, runtimeStore, applyJournal);

        return new DeploymentApply(promotion.TargetRevision, run, apply);
    }

    private static async Task<WorkflowArtifactCommandProcessResult> ProcessNextRuntimeCommandAsync(
        HttpClient owner,
        Guid workspaceId,
        Guid engineId,
        InMemoryPayloadFetcher payloads,
        InMemoryWorkflowDefinitionRuntimeStore runtimeStore,
        InMemoryWorkflowArtifactApplyJournal applyJournal)
    {
        var options = new WorkflowArtifactRuntimeOptions
        {
            PlatformEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            EngineId = engineId,
            WorkerId = "artifact-e2e-worker",
            RuntimeVersion = "4.0.0",
            ClaimLeaseDuration = TimeSpan.FromMinutes(5),
            HeartbeatInterval = TimeSpan.FromSeconds(30)
        };
        var commandClient = new WorkflowRuntimeCommandHttpClient(owner, options);
        var polled = await commandClient.PollAsync(limit: 1);
        var command = polled.Should().ContainSingle().Subject;
        var claim = await commandClient.ClaimAsync(command.Id);
        claim.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);

        var processor = new WorkflowArtifactCommandProcessor(
            commandClient,
            new WorkflowArtifactPlatformEnvelopeClient(owner, options),
            payloads,
            new WorkflowArtifactRuntimeContractValidator(options),
            new JournaledWorkflowDefinitionApplier(new WorkflowDefinitionJsonApplier(runtimeStore), applyJournal),
            options);

        return await processor.ProcessAsync(claim.Claim!);
    }

    private static async Task<DeploymentTopology> SeedTopologyAsync(PlatformApiTestApplication app, Guid workspaceId)
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
                "https://runtime.example.test",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/runtime",
                [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
                [],
                "container-apps"));

        return new DeploymentTopology(application, sourceEnvironment, targetEnvironment, targetEngine);
    }

    private static async Task<ActionConfirmation> CreateConfirmationAsync(
        HttpClient owner,
        Guid workspaceId,
        ConfirmationActionType actionType,
        Guid targetId)
    {
        var response = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(actionType, targetId.ToString("D"), null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadPlatformJsonAsync<ActionConfirmation>())!;
    }

    private static WorkspaceDesiredStateRecordRequest ArtifactReferenceRecord(WorkspaceArtifact artifact) =>
        Record(
            DesiredStateRecordKind.ArtifactReference,
            "Payment Retry",
            $$"""
            {
              "artifactRecordId": "{{artifact.Id:D}}",
              "artifactId": "{{artifact.ArtifactId}}",
              "artifactTypeId": "{{artifact.ArtifactTypeId}}",
              "contentDigest": {
                "algorithm": "{{artifact.ContentDigest.Algorithm}}",
                "value": "{{artifact.ContentDigest.Value}}"
              }
            }
            """);

    private static WorkspaceDesiredStateRecordRequest Record(
        DesiredStateRecordKind kind,
        string name,
        string payloadJson) =>
        new(kind, name, JsonSerializer.Deserialize<JsonElement>(payloadJson, PlatformApiTestApplication.JsonOptions));

    private sealed class InMemoryPayloadFetcher : IWorkflowArtifactPayloadFetcher
    {
        private readonly Dictionary<string, string> _payloads = new(StringComparer.Ordinal);

        public void Add(string uri, string workflowJson) => _payloads[uri] = workflowJson;

        public Task<WorkflowArtifactPayload> FetchAsync(
            ArtifactPayloadReference reference,
            CancellationToken cancellationToken = default)
        {
            _payloads.TryGetValue(reference.Uri, out var workflowJson).Should().BeTrue($"payload {reference.Uri} should be available to the smoke runtime");
            return Task.FromResult(new WorkflowArtifactPayload(
                reference,
                Encoding.UTF8.GetBytes(workflowJson!),
                reference.MediaType));
        }
    }

    private sealed record SubmittedArtifact(
        WorkspaceArtifact Artifact,
        StudioSubmitResult Submit,
        StudioSubmitResult Duplicate);

    private sealed record DeploymentTopology(
        WorkspaceDeploymentApplication Application,
        WorkspaceDeploymentEnvironment SourceEnvironment,
        WorkspaceDeploymentEnvironment TargetEnvironment,
        WorkspaceWorkflowEngine TargetEngine);

    private sealed record DeploymentApply(
        WorkspaceDesiredStateRevision TargetRevision,
        WorkspaceDeploymentRun Run,
        WorkflowArtifactCommandProcessResult Apply);
}
