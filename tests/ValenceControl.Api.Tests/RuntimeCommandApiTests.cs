using System.Net;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ValenceControl.Workflows.RuntimeApplier;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class RuntimeCommandApiTests
{
    [Fact]
    public async Task Runtime_applier_client_can_poll_claim_report_progress_and_complete_command()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-applier-client-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-applier-client-owner");
        var client = new WorkflowRuntimeCommandHttpClient(owner, new WorkflowArtifactRuntimeOptions
        {
            ControlEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            EngineId = seeded.EngineId,
            WorkerId = "runtime-applier-worker",
            ClaimLeaseDuration = TimeSpan.FromSeconds(90)
        });

        var polled = await client.PollAsync();
        var command = polled.Single();
        var claim = await client.ClaimAsync(command.Id);
        var progress = await client.ReportProgressAsync(command.Id, claim.Claim!.LeaseToken, "applying", 50, "Applying workflow artifact");
        var complete = await client.CompleteAsync(
            command.Id,
            claim.Claim.LeaseToken,
            new ArtifactDigest("sha256", "observed"),
            "elsa://workflows/payment-retry",
            []);
        var detail = await owner.GetControlJsonAsync<WorkspaceDeploymentRunDetailResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runs/{seeded.RunId}");

        claim.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        progress.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        complete.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        complete.Command!.Status.Should().Be(WorkflowRuntimeCommandStatus.Completed);
        detail!.Run.Status.Should().Be(WorkspaceDeploymentRunStatus.Succeeded);
        detail.Commands.Single().WorkerId.Should().Be("runtime-applier-worker");
        detail.Commands.Single().RuntimeReference.Should().Be("elsa://workflows/payment-retry");
    }

    [Fact]
    public async Task Runtime_applier_client_can_use_engine_secret_without_workspace_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-engine-secret-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunWithLocalEngineSecretAsync(app, owner, workspaceId, "runtime-engine-secret-owner", "engine-secret-token");
        var anonymous = app.CreateClient();
        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/workspaces/{workspaceId:D}/deployments/runtime/engines/{seeded.EngineId:D}/commands");
        deniedRequest.Headers.Add("X-Elsa-Engine-Secret", "wrong-secret");
        var denied = await anonymous.SendAsync(deniedRequest);
        var client = new WorkflowRuntimeCommandHttpClient(anonymous, new WorkflowArtifactRuntimeOptions
        {
            ControlEndpoint = anonymous.BaseAddress!,
            WorkspaceId = workspaceId,
            EngineId = seeded.EngineId,
            EngineSecret = "engine-secret-token",
            WorkerId = "runtime-secret-worker",
            ClaimLeaseDuration = TimeSpan.FromSeconds(90)
        });

        var polled = await client.PollAsync();
        var command = polled.Single();
        var claim = await client.ClaimAsync(command.Id);
        var complete = await client.CompleteAsync(
            command.Id,
            claim.Claim!.LeaseToken,
            null,
            "elsa://workflows/secret-authorized",
            []);

        denied.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        claim.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        complete.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        complete.Command!.RuntimeReference.Should().Be("elsa://workflows/secret-authorized");
    }

    [Fact]
    public async Task Runtime_can_poll_claim_report_progress_and_complete_command()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-owner");

        var polled = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();
        var claimResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));
        var claim = await claimResponse.Content.ReadControlJsonAsync<RuntimeCommandClaimResponse>();
        var progressResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/progress",
            new RuntimeCommandProgressRequest(claim!.LeaseToken, "applying", 75, "Applying workflow definitions"));
        var completeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest(
                claim.LeaseToken,
                new WorkspaceArtifactDigest("sha256", "observed"),
                "elsa://workflows/payment-retry",
                []));
        var duplicateCompleteResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest(
                claim.LeaseToken,
                new WorkspaceArtifactDigest("sha256", "observed"),
                "elsa://workflows/payment-retry",
                []));
        var wrongLeaseCompleteResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest("duplicate-delivery", null, "elsa://workflows/payment-retry", []));
        var detail = await owner.GetControlJsonAsync<WorkspaceDeploymentRunDetailResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runs/{seeded.RunId}");

        claimResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        progressResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        duplicateCompleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        wrongLeaseCompleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        detail!.Run.Status.Should().Be(WorkspaceDeploymentRunStatus.Succeeded);
        detail.History.Should().Contain(x => x.Message == "Runtime command completed.");
        detail.Commands.Should().ContainSingle();
        var summary = detail.Commands.Single();
        summary.Id.Should().Be(command.Id);
        summary.Status.Should().Be(DeploymentCommandStatus.Completed);
        summary.ProgressMessage.Should().Be("Applying workflow definitions");
        summary.PercentComplete.Should().Be(75);
        summary.RuntimeReference.Should().Be("elsa://workflows/payment-retry");
        summary.ObservedArtifactDigest.Should().Be(new WorkspaceArtifactDigest("sha256", "observed"));
        summary.WorkerId.Should().Be("runtime-worker-1");
    }

    [Fact]
    public async Task Runtime_can_download_artifact_with_active_lease_and_report_per_artifact_outcome()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-artifact-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var artifactBytes = "artifact payload"u8.ToArray();
        var seeded = await SeedArtifactBackedRunAsync(app, owner, workspaceId, "runtime-artifact-owner", artifactBytes);
        var polled = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();
        var claimResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));
        var claim = await claimResponse.Content.ReadControlJsonAsync<RuntimeCommandClaimResponse>();
        var artifactItem = claim!.Command.Artifacts!.Single();
        var missingLeaseResponse = await owner.GetAsync(artifactItem.DownloadUrl);
        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, artifactItem.DownloadUrl);
        downloadRequest.Headers.Add("X-Elsa-Command-Lease", claim.LeaseToken);
        downloadRequest.Headers.Add("X-Elsa-Worker-Id", "runtime-worker-1");
        using var downloadResponse = await owner.SendAsync(downloadRequest);
        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync();
        var completeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest(
                claim.LeaseToken,
                seeded.Artifact.ContentDigest,
                "elsa://workflows/claims-prod",
                [],
                [
                    new DeploymentCommandArtifactOutcome(
                        seeded.Artifact.Id,
                        DeploymentCommandArtifactStatus.Applied,
                        seeded.Artifact.ContentDigest,
                        "elsa://workflows/claims-prod")
                ]));
        var completed = await completeResponse.Content.ReadControlJsonAsync<RuntimeCommandDto>();

        claimResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        artifactItem.DownloadUrl.Should().Be($"/api/workspaces/{workspaceId:D}/deployments/runtime/commands/{command.Id:D}/artifacts/{seeded.Artifact.Id:D}/download");
        missingLeaseResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        downloadResponse.Headers.GetValues("X-Elsa-Artifact-Digest").Single().Should().Be(seeded.Artifact.ContentDigest.Value);
        downloaded.Should().Equal(artifactBytes);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        completed!.Status.Should().Be(DeploymentCommandStatus.Completed);
        completed.Artifacts!.Single().Status.Should().Be(DeploymentCommandArtifactStatus.Applied);
        completed.Artifacts!.Single().DownloadUrl.Should().Be(artifactItem.DownloadUrl);
    }

    [Fact]
    public async Task Runtime_claim_conflicts_when_command_is_already_leased()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-conflict-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-conflict-owner");
        var polled = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();

        var first = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));
        var second = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-2", 300));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Runtime_can_fail_or_reject_with_safe_diagnostics()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-fail-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var failed = await ClaimNextCommandAsync(app, owner, workspaceId, "runtime-fail-owner", "runtime-worker-1");

        var failResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{failed.Command.Id}/fail",
            new RuntimeCommandFailRequest(
                failed.LeaseToken,
                [new DeploymentCommandDiagnostic("apply-failed", DeploymentCommandDiagnosticSeverity.Error, "bearer token leaked")]));
        var failBody = await failResponse.Content.ReadControlJsonAsync<RuntimeCommandDto>();
        var rejected = await ClaimNextCommandAsync(app, owner, workspaceId, "runtime-fail-owner", "runtime-worker-2");
        var rejectResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{rejected.Command.Id}/reject",
            new RuntimeCommandRejectRequest(
                rejected.LeaseToken,
                [new DeploymentCommandDiagnostic("unsupported", DeploymentCommandDiagnosticSeverity.Warning, "private key missing")]));
        var rejectBody = await rejectResponse.Content.ReadControlJsonAsync<RuntimeCommandDto>();

        failResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        failBody!.Status.Should().Be(DeploymentCommandStatus.Failed);
        failBody.Diagnostics.Single().Message.Should().Be("[redacted] [redacted] leaked");
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rejectBody!.Status.Should().Be(DeploymentCommandStatus.Rejected);
        rejectBody.Diagnostics.Single().Message.Should().Be("[redacted] missing");
    }

    [Fact]
    public async Task Webhook_notification_is_safe_trigger_and_runtime_must_poll_to_claim()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-webhook-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-webhook-owner");
        var polled = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();

        var firstNotificationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/webhook-notifications",
            new RuntimeCommandWebhookNotificationRequest(seeded.EngineId));
        var secondNotificationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/webhook-notifications",
            new RuntimeCommandWebhookNotificationRequest(seeded.EngineId));
        var firstNotification = await firstNotificationResponse.Content.ReadControlJsonAsync<RuntimeCommandWebhookNotificationResponse>();
        var afterWebhookPoll = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var claimResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));

        firstNotificationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondNotificationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstNotification!.WorkspaceId.Should().Be(workspaceId);
        firstNotification.EngineId.Should().Be(seeded.EngineId);
        firstNotification.CommandHint.Should().Be(command.Id);
        firstNotification.SafePayloadJson.Should().Contain(command.Id.ToString("D"));
        firstNotification.SafePayloadJson.ToLowerInvariant().Should().NotContain("lease");
        firstNotification.SafePayloadJson.ToLowerInvariant().Should().NotContain("secret");
        afterWebhookPoll!.Commands.Should().ContainSingle(x => x.Id == command.Id);
        claimResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<RuntimeCommandClaimResponse> ClaimNextCommandAsync(
        ControlApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject,
        string workerId)
    {
        var seeded = await SeedRunAsync(app, workspaceId, subject);
        var polled = await owner.GetControlJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();
        var claimResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, workerId, 300));
        return (await claimResponse.Content.ReadControlJsonAsync<RuntimeCommandClaimResponse>())!;
    }

    private static async Task<SeededRun> SeedRunWithLocalEngineSecretAsync(
        ControlApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject,
        string engineSecret)
    {
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest($"Claims {Guid.NewGuid():N}", null));
        var application = (await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>())!;
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{application.Id:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production));
        var environment = (await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>())!;
        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest(
                "Local engine credentials",
                null,
                null,
                DeploymentSecretStoreType.LocalEncryptedDatabase));
        var secretStore = (await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>())!;
        var referenceResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/secret-stores/{secretStore.Id:D}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest(
                "Runtime engine API",
                $"local://engine-credentials/{Guid.NewGuid():N}",
                null,
                engineSecret));
        var reference = (await referenceResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>())!;
        var engineResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/environments/{environment.Id:D}/engines",
            new WorkspaceWorkflowEngineRequest(
                "claims-prod",
                "https://runtime.example.test",
                "westeurope",
                null,
                null,
                [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
                [],
                "container-apps",
                reference.Id));
        var engine = (await engineResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>())!;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var accountId = await db.ExternalIdentities
            .Where(x => x.Issuer == WorkspaceDeploymentTestFixtures.DefaultIssuer && x.Subject == subject)
            .Select(x => x.AccountId)
            .SingleAsync();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var mutationStore = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        var revision = await store.CreateRevisionAsync(
            workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "v1", "abc123", "{\"records\":[]}", accountId));
        var run = await mutationStore.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(revision.Id, environment.Id, engine.Id, Guid.NewGuid(), accountId, null),
            DateTimeOffset.UtcNow);
        return new SeededRun(engine.Id, run.Id);
    }

    private static async Task<SeededRun> SeedRunAsync(ControlApiTestApplication app, Guid workspaceId, string subject)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var accountId = await db.ExternalIdentities
            .Where(x => x.Issuer == WorkspaceDeploymentTestFixtures.DefaultIssuer && x.Subject == subject)
            .Select(x => x.AccountId)
            .SingleAsync();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var mutationStore = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();

        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Claims {Guid.NewGuid():N}", null, accountId));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://runtime.example.test",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/runtime",
                [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
                [],
                "container-apps"));
        var revision = await store.CreateRevisionAsync(
            workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "v1", "abc123", "{\"records\":[]}", accountId));
        var run = await mutationStore.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(revision.Id, environment.Id, engine.Id, Guid.NewGuid(), accountId, null),
            DateTimeOffset.UtcNow);
        return new SeededRun(engine.Id, run.Id);
    }

    private static async Task<SeededArtifactRun> SeedArtifactBackedRunAsync(
        ControlApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject,
        byte[] artifactBytes)
    {
        var artifactPath = Path.Combine(Path.GetTempPath(), $"elsa-runtime-artifact-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes);
        var registerResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:runtime-download", artifactPath) with { Format = WorkspaceArtifactFormat.Zip });
        var artifact = (await registerResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var accountId = await db.ExternalIdentities
            .Where(x => x.Issuer == WorkspaceDeploymentTestFixtures.DefaultIssuer && x.Subject == subject)
            .Select(x => x.AccountId)
            .SingleAsync();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var mutationStore = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();

        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Claims {Guid.NewGuid():N}", null, accountId));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://runtime.example.test",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/runtime",
                [new EngineCapability("loom.recipe.apply", "Apply Loom recipes", CapabilityBoundary.EngineApi)],
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
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "v1", "abc123", desiredStateJson, accountId));
        var run = await mutationStore.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(revision.Id, environment.Id, engine.Id, Guid.NewGuid(), accountId, null),
            DateTimeOffset.UtcNow);
        return new SeededArtifactRun(engine.Id, run.Id, artifact);
    }

    private sealed record SeededRun(Guid EngineId, Guid RunId);
    private sealed record SeededArtifactRun(Guid EngineId, Guid RunId, WorkspaceArtifact Artifact);
}
