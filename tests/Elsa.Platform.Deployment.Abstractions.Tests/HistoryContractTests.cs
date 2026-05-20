using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.History;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Abstractions.Targets;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Abstractions.Tests;

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

        result.Status.Should().Be(DeploymentStatus.PartiallyApplied);
        result.ResourceResults.Should().ContainSingle().Which.Retryable.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle().Which.Should().Be(diagnostic);
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

        history.DeploymentId.Should().Be("deploy-1");
        history.Artifact.Should().Be(_artifact);
        history.ManifestDigest.Should().Be(_artifact.ManifestDigest);
        history.Target.Should().Be(_target);
        history.Actor.Should().Be(_actor);
        history.Plan.Should().Be(plan);
        history.ResourceResults.Should().ContainSingle().Which.Should().Be(resourceResult);
    }
}
