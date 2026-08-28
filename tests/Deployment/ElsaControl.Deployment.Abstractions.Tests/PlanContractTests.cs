using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Resources;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.Tests;

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

        Assert.Equal("plan-1", plan.Id);
        Assert.Equal(_artifact, plan.Artifact);
        Assert.Equal(_target, plan.Target);
        Assert.Equal(change, Assert.Single(plan.Changes));
        Assert.Equal(diagnostic, Assert.Single(plan.Diagnostics));
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

        Assert.Equal(action, change.Action);
        Assert.Equal(DeploymentChangeStatus.Pending, change.Status);
    }
}
