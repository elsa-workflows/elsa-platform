using FluentAssertions;

namespace ValenceControl.Deployment.Artifacts.Tests;

public class ArtifactPathValidationTests
{
    [Theory]
    [InlineData("workflows/order.json", "workflows/order.json")]
    [InlineData("recipes\\init.yaml", "recipes/init.yaml")]
    public void NormalizesValidRelativePath(string path, string expected)
    {
        DeploymentArtifactPathValidator.NormalizeRelativePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("workflows/../escape.json")]
    [InlineData("/absolute/path.json")]
    [InlineData("C:\\absolute\\path.json")]
    [InlineData("workflows//order.json")]
    [InlineData("workflows/./order.json")]
    public void RejectsInvalidRelativePath(string path)
    {
        DeploymentArtifactPathValidator.NormalizeRelativePath(path).Should().BeNull();
    }
}
