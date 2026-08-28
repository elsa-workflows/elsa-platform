using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.History;
using ElsaControl.Deployment.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Resources;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.Tests;

public class HistoryContractTests
{
    private readonly DeploymentResourceId _workflow = new("workflowDefinition", "order-approval");
    private readonly DeploymentTargetDescriptor _target = new("staging", environment: "staging");
    private readonly DeploymentActor _actor = new("user:alice", "Alice");
    private readonly DeploymentArtifactIdentity _artifact = new(
        "sales",
        "v1alpha",
        new ArtifactDigest("sha256", "manifest"),
        new ArtifactDigest("sha256", "content"));

    [Fact]
    public void DeploymentResultCapturesPartialFailureAndRetryabilityPerResource()
    {
        var diagnostic = new DeploymentDiagnostic("apply.failed", DeploymentDiagnosticSeverity.Error, "Apply failed.", _workflow);
        var resourceResult = new DeploymentResourceResult(
            _workflow,
            DeploymentChangeAction.Update,
            DeploymentChangeStatus.Failed,
            retryable: true,
            diagnostics: [diagnostic]);

        var result = new DeploymentResult(
            "deploy-1",
            DeploymentOperationMode.Apply,
            DeploymentStatus.PartiallyApplied,
            _artifact,
            _target,
            resourceResults: [resourceResult],
            diagnostics: [diagnostic]);

        Assert.Equal(DeploymentStatus.PartiallyApplied, result.Status);
        Assert.True(Assert.Single(result.ResourceResults).Retryable);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void HistoryRecordCapturesAuditFieldsAndManifestDigest()
    {
        var change = new DeploymentChange(_workflow, DeploymentChangeAction.NoOp, DeploymentChangeStatus.Completed);
        var plan = new DeploymentPlan("plan-1", _artifact, _target, [change]);
        var resourceResult = new DeploymentResourceResult(_workflow, DeploymentChangeAction.NoOp, DeploymentChangeStatus.Completed);

        var history = new DeploymentHistoryRecord(
            "deploy-1",
            _artifact,
            _target,
            DeploymentStatus.NoOp,
            _actor,
            plan,
            [resourceResult]);

        Assert.Equal("deploy-1", history.DeploymentId);
        Assert.Equal(_artifact, history.Artifact);
        Assert.Equal(_artifact.ManifestDigest, history.ManifestDigest);
        Assert.Equal(_target, history.Target);
        Assert.Equal(_actor, history.Actor);
        Assert.Equal(plan, history.Plan);
        Assert.Equal(resourceResult, Assert.Single(history.ResourceResults));
    }

    [Fact]
    public void HistoryRecordLeavesCompletionTimeUnsetWhenNoCompletionWasRecorded()
    {
        var history = new DeploymentHistoryRecord(
            "deploy-1",
            _artifact,
            _target,
            DeploymentStatus.NotStarted,
            startedAt: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(TimeSpan.Zero, history.StartedAt.Offset);
        Assert.Null(history.CompletedAt);
    }
}
