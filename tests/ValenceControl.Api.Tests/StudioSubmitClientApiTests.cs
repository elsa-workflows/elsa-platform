using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Studio.Submit;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

public sealed class StudioSubmitClientApiTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();

    [Fact]
    public async Task Studio_submit_client_registers_and_deduplicates_artifact_without_deployment_run()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("studio-submit-client-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var options = new StudioSubmitOptions
        {
            ControlEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            ProducerVersion = "4.0.0",
            RuntimeVersionRange = ">=4.0.0"
        };
        var package = _packager.Package(Snapshot(), options);
        var submitClient = new StudioControlArtifactSubmitClient(owner);

        var submitted = await submitClient.SubmitAsync(package, options);
        var duplicate = await submitClient.SubmitAsync(package, options);
        var artifacts = await owner.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts");
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        submitted.Status.Should().Be(StudioSubmitStatus.Submitted);
        duplicate.Status.Should().Be(StudioSubmitStatus.Duplicate);
        submitted.ArtifactId.Should().Be(package.Envelope.ArtifactId);
        duplicate.ArtifactId.Should().Be(package.Envelope.ArtifactId);
        submitted.ArtifactDigest.Should().Be($"{package.Envelope.ContentDigest.Algorithm}:{package.Envelope.ContentDigest.Value}");
        artifacts!.Items.Should().ContainSingle(x => x.ArtifactId == package.Envelope.ArtifactId)
            .Which.Producer!.ProducerType.Should().Be("studio");
        artifacts.Items.Single().ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaLoomRecipe);
        artifacts.Items.Single().DisplayMetadata!.Source.Should().Be("studio://workflows/payment-retry");
        cockpit!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Studio_submit_client_uploads_artifact_and_creates_desired_state_revision()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("studio-submit-revision-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = (await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>())!;
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Dev", EnvironmentTier.Dev));
        var environment = (await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>())!;
        var options = new StudioSubmitOptions
        {
            ControlEndpoint = owner.BaseAddress!,
            WorkspaceId = workspaceId,
            ApplicationId = application.Id,
            EnvironmentId = environment.Id,
            ProducerVersion = "4.0.0",
            RuntimeVersionRange = ">=4.0.0"
        };
        var package = _packager.Package(Snapshot(), options);
        var submitClient = new StudioControlArtifactSubmitClient(owner);

        var submitted = await submitClient.SubmitRevisionAsync(package, options);
        var artifacts = await owner.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts");
        var revisions = await owner.GetControlJsonAsync<WorkspaceApplicationRevisionsResponse>(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/revisions");

        submitted.Status.Should().Be(StudioSubmitStatus.Submitted);
        submitted.ArtifactRecordId.Should().NotBeNull();
        submitted.RevisionId.Should().NotBeNull();
        artifacts!.Items.Should().ContainSingle(x => x.Id == submitted.ArtifactRecordId)
            .Which.Format.Should().Be(WorkspaceArtifactFormat.Zip);
        revisions!.Items.Should().ContainSingle()
            .Which.Revision.Id.Should().Be(submitted.RevisionId!.Value);
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
