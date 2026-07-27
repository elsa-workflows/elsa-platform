using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Studio.Submit;

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

        Assert.Equal(StudioSubmitStatus.Submitted, submitted.Status);
        Assert.Equal(StudioSubmitStatus.Duplicate, duplicate.Status);
        Assert.Equal(package.Envelope.ArtifactId, submitted.ArtifactId);
        Assert.Equal(package.Envelope.ArtifactId, duplicate.ArtifactId);
        Assert.Equal($"{package.Envelope.ContentDigest.Algorithm}:{package.Envelope.ContentDigest.Value}", submitted.ArtifactDigest);
        var artifact = Assert.Single(artifacts!.Items, x => x.ArtifactId == package.Envelope.ArtifactId);
        Assert.Equal("studio", artifact.Producer!.ProducerType);
        Assert.Equal(ArtifactTypeIds.ElsaLoomRecipe, artifacts.Items.Single().ArtifactTypeId);
        Assert.Equal("studio://workflows/payment-retry", artifacts.Items.Single().DisplayMetadata!.Source);
        Assert.Empty(cockpit!.History);
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

        Assert.Equal(StudioSubmitStatus.Submitted, submitted.Status);
        Assert.NotNull(submitted.ArtifactRecordId);
        Assert.NotNull(submitted.RevisionId);
        var artifact = Assert.Single(artifacts!.Items, x => x.Id == submitted.ArtifactRecordId);
        Assert.Equal(WorkspaceArtifactFormat.Zip, artifact.Format);
        var revision = Assert.Single(revisions!.Items);
        Assert.Equal(submitted.RevisionId!.Value, revision.Revision.Id);
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
