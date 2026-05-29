using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Studio.Submit;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class StudioSubmitClientApiTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();

    [Fact]
    public async Task Studio_submit_client_registers_and_deduplicates_artifact_without_deployment_run()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("studio-submit-client-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var options = new StudioSubmitOptions
        {
            PlatformEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            ProducerVersion = "4.0.0",
            RuntimeVersionRange = ">=4.0.0"
        };
        var package = _packager.Package(Snapshot(), options);
        var submitClient = new StudioPlatformArtifactSubmitClient(owner);

        var submitted = await submitClient.SubmitAsync(package, options);
        var duplicate = await submitClient.SubmitAsync(package, options);
        var artifacts = await owner.GetPlatformJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts");
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        submitted.Status.Should().Be(StudioSubmitStatus.Submitted);
        duplicate.Status.Should().Be(StudioSubmitStatus.Duplicate);
        submitted.ArtifactId.Should().Be(package.Envelope.ArtifactId);
        duplicate.ArtifactId.Should().Be(package.Envelope.ArtifactId);
        submitted.ArtifactDigest.Should().Be($"{package.Envelope.ContentDigest.Algorithm}:{package.Envelope.ContentDigest.Value}");
        artifacts!.Items.Should().ContainSingle(x => x.ArtifactId == package.Envelope.ArtifactId)
            .Which.Producer!.ProducerType.Should().Be("studio");
        artifacts.Items.Single().ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        artifacts.Items.Single().DisplayMetadata!.Source.Should().Be("studio://workflows/payment-retry");
        cockpit!.History.Should().BeEmpty();
    }

    private static WorkflowSubmissionSnapshot Snapshot() =>
        new(
            "payment-retry",
            "v42",
            "Payment Retry",
            "42",
            "Retries payment collection failures.",
            """{"id":"payment-retry","name":"PaymentRetry","version":42}""",
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            "studio://workflows/payment-retry",
            new Dictionary<string, string> { ["domain"] = "payments" },
            new Dictionary<string, string> { ["owner"] = "finance-ops" });
}
