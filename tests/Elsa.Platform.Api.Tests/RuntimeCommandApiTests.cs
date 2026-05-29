using System.Net;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class RuntimeCommandApiTests
{
    [Fact]
    public async Task Runtime_applier_client_can_poll_claim_report_progress_and_complete_command()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-applier-client-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-applier-client-owner");
        var client = new WorkflowRuntimeCommandHttpClient(owner, new WorkflowArtifactRuntimeOptions
        {
            PlatformEndpoint = owner.BaseAddress!,
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
        var detail = await owner.GetPlatformJsonAsync<WorkspaceDeploymentRunDetailResponse>(
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
    public async Task Runtime_can_poll_claim_report_progress_and_complete_command()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-owner");

        var polled = await owner.GetPlatformJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();
        var claimResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));
        var claim = await claimResponse.Content.ReadPlatformJsonAsync<RuntimeCommandClaimResponse>();
        var progressResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/progress",
            new RuntimeCommandProgressRequest(claim!.LeaseToken, "applying", 75, "Applying workflow definitions"));
        var completeResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest(
                claim.LeaseToken,
                new WorkspaceArtifactDigest("sha256", "observed"),
                "elsa://workflows/payment-retry",
                []));
        var duplicateCompleteResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest(
                claim.LeaseToken,
                new WorkspaceArtifactDigest("sha256", "observed"),
                "elsa://workflows/payment-retry",
                []));
        var wrongLeaseCompleteResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/complete",
            new RuntimeCommandCompleteRequest("duplicate-delivery", null, "elsa://workflows/payment-retry", []));
        var detail = await owner.GetPlatformJsonAsync<WorkspaceDeploymentRunDetailResponse>(
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
    public async Task Runtime_claim_conflicts_when_command_is_already_leased()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-conflict-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-conflict-owner");
        var polled = await owner.GetPlatformJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();

        var first = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-1", 300));
        var second = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, "runtime-worker-2", 300));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Runtime_can_fail_or_reject_with_safe_diagnostics()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-fail-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var failed = await ClaimNextCommandAsync(app, owner, workspaceId, "runtime-fail-owner", "runtime-worker-1");

        var failResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{failed.Command.Id}/fail",
            new RuntimeCommandFailRequest(
                failed.LeaseToken,
                [new DeploymentCommandDiagnostic("apply-failed", DeploymentCommandDiagnosticSeverity.Error, "bearer token leaked")]));
        var failBody = await failResponse.Content.ReadPlatformJsonAsync<RuntimeCommandDto>();
        var rejected = await ClaimNextCommandAsync(app, owner, workspaceId, "runtime-fail-owner", "runtime-worker-2");
        var rejectResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{rejected.Command.Id}/reject",
            new RuntimeCommandRejectRequest(
                rejected.LeaseToken,
                [new DeploymentCommandDiagnostic("unsupported", DeploymentCommandDiagnosticSeverity.Warning, "private key missing")]));
        var rejectBody = await rejectResponse.Content.ReadPlatformJsonAsync<RuntimeCommandDto>();

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
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("runtime-webhook-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var seeded = await SeedRunAsync(app, workspaceId, "runtime-webhook-owner");
        var polled = await owner.GetPlatformJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();

        var firstNotificationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/webhook-notifications",
            new RuntimeCommandWebhookNotificationRequest(seeded.EngineId));
        var secondNotificationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/webhook-notifications",
            new RuntimeCommandWebhookNotificationRequest(seeded.EngineId));
        var firstNotification = await firstNotificationResponse.Content.ReadPlatformJsonAsync<RuntimeCommandWebhookNotificationResponse>();
        var afterWebhookPoll = await owner.GetPlatformJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var claimResponse = await owner.PostPlatformJsonAsync(
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
        PlatformApiTestApplication app,
        HttpClient owner,
        Guid workspaceId,
        string subject,
        string workerId)
    {
        var seeded = await SeedRunAsync(app, workspaceId, subject);
        var polled = await owner.GetPlatformJsonAsync<RuntimeCommandListResponse>(
            $"/api/workspaces/{workspaceId}/deployments/runtime/engines/{seeded.EngineId}/commands");
        var command = polled!.Commands.Single();
        var claimResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runtime/commands/{command.Id}/claim",
            new RuntimeCommandClaimRequest(seeded.EngineId, workerId, 300));
        return (await claimResponse.Content.ReadPlatformJsonAsync<RuntimeCommandClaimResponse>())!;
    }

    private static async Task<SeededRun> SeedRunAsync(PlatformApiTestApplication app, Guid workspaceId, string subject)
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

    private sealed record SeededRun(Guid EngineId, Guid RunId);
}
