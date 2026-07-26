using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace ValenceControl.Deployment.Abstractions.Tests;

public class ResourceIdentityTests
{
    private readonly DeploymentResourceId _workflow = new(" workflowDefinition ", " order-approval ", " sales ");
    private readonly DeploymentResourceId _variable = new("variable", "orderTimeout");

    [Fact]
    public void ResourceIdentityNormalizesRequiredParts()
    {
        _workflow.Type.Should().Be("workflowDefinition");
        _workflow.LogicalId.Should().Be("order-approval");
        _workflow.Scope.Should().Be("sales");
        _workflow.ToString().Should().Be("sales:workflowDefinition/order-approval");
    }

    [Theory]
    [InlineData("", "order-approval")]
    [InlineData("workflowDefinition", " ")]
    public void ResourceIdentityRejectsEmptyRequiredParts(string type, string logicalId)
    {
        var act = () => new DeploymentResourceId(type, logicalId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeploymentResourceKeepsConservativeDefaults()
    {
        var resource = new DeploymentResource(
            _workflow,
            version: "1",
            desiredStateHash: new ArtifactDigest("sha256", "abc"),
            dependencies: [_variable]);

        resource.Deletion.Should().Be(DeploymentDeletionBehavior.Retain);
        resource.Dependencies.Should().ContainSingle().Which.Should().Be(_variable);
        resource.DesiredStateHash.Should().Be(new ArtifactDigest("sha256", "abc"));
    }

    [Fact]
    public void DeploymentResourceStateDescribesTargetStateWithoutDesiredPayload()
    {
        var state = new DeploymentResourceState(_variable, new ArtifactDigest("sha256", "def"), version: "current");

        state.Id.Should().Be(_variable);
        state.StateHash.Should().Be(new ArtifactDigest("sha256", "def"));
        state.Version.Should().Be("current");
    }
}
