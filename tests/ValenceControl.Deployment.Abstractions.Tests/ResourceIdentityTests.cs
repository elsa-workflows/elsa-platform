using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Resources;

namespace ValenceControl.Deployment.Abstractions.Tests;

public class ResourceIdentityTests
{
    private readonly DeploymentResourceId _workflow = new(" workflowDefinition ", " order-approval ", " sales ");
    private readonly DeploymentResourceId _variable = new("variable", "orderTimeout");

    [Fact]
    public void ResourceIdentityNormalizesRequiredParts()
    {
        Assert.Equal("workflowDefinition", _workflow.Type);
        Assert.Equal("order-approval", _workflow.LogicalId);
        Assert.Equal("sales", _workflow.Scope);
        Assert.Equal("sales:workflowDefinition/order-approval", _workflow.ToString());
    }

    [Theory]
    [InlineData("", "order-approval")]
    [InlineData("workflowDefinition", " ")]
    public void ResourceIdentityRejectsEmptyRequiredParts(string type, string logicalId)
    {
        var act = () => new DeploymentResourceId(type, logicalId);

        Assert.Throws<ArgumentException>(() => _ = act());
    }

    [Fact]
    public void DeploymentResourceKeepsConservativeDefaults()
    {
        var resource = new DeploymentResource(
            _workflow,
            version: "1",
            desiredStateHash: new ArtifactDigest("sha256", "abc"),
            dependencies: [_variable]);

        Assert.Equal(DeploymentDeletionBehavior.Retain, resource.Deletion);
        Assert.Equal(_variable, Assert.Single(resource.Dependencies));
        Assert.Equal(new ArtifactDigest("sha256", "abc"), resource.DesiredStateHash);
    }

    [Fact]
    public void DeploymentResourceStateDescribesTargetStateWithoutDesiredPayload()
    {
        var state = new DeploymentResourceState(_variable, new ArtifactDigest("sha256", "def"), version: "current");

        Assert.Equal(_variable, state.Id);
        Assert.Equal(new ArtifactDigest("sha256", "def"), state.StateHash);
        Assert.Equal("current", state.Version);
    }
}
