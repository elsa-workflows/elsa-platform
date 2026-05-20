using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Abstractions.Targets;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Abstractions.Tests;

public class PlanContractTests
{
    private readonly DeploymentResourceId _workflow = new("workflowDefinition", "order-approval");
    private readonly DeploymentTargetDescriptor _target = new("staging", environment: "staging");
    private readonly DeploymentArtifactIdentity _artifact = new(
        "sales",
        "v1alpha",
        new ArtifactDigest("sha256", "manifest"),
        new ArtifactDigest("sha256", "content"));

    [Fact]
    public void PlanGroupsArtifactTargetChangesAndDiagnostics()
    {
        var change = new DeploymentChange(_workflow, DeploymentChangeAction.Update, DeploymentChangeStatus.Ready);
        var diagnostic = new DeploymentDiagnostic("plan.ready", DeploymentDiagnosticSeverity.Info, "Plan is ready.");

        var plan = new DeploymentPlan("plan-1", _artifact, _target, [change], [diagnostic]);

        plan.Id.Should().Be("plan-1");
        plan.Artifact.Should().Be(_artifact);
        plan.Target.Should().Be(_target);
        plan.Changes.Should().ContainSingle().Which.Should().Be(change);
        plan.Diagnostics.Should().ContainSingle().Which.Should().Be(diagnostic);
    }

    [Theory]
    [InlineData(DeploymentChangeAction.Create)]
    [InlineData(DeploymentChangeAction.Update)]
    [InlineData(DeploymentChangeAction.Activate)]
    [InlineData(DeploymentChangeAction.Deactivate)]
    [InlineData(DeploymentChangeAction.Delete)]
    [InlineData(DeploymentChangeAction.NoOp)]
    [InlineData(DeploymentChangeAction.Unsupported)]
    [InlineData(DeploymentChangeAction.Conflict)]
    public void ChangeModelRepresentsPhaseOneActionTaxonomy(DeploymentChangeAction action)
    {
        var change = new DeploymentChange(_workflow, action);

        change.Action.Should().Be(action);
        change.Status.Should().Be(DeploymentChangeStatus.Pending);
    }
}
