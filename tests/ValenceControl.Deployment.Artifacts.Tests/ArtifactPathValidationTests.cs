
namespace ValenceControl.Deployment.Artifacts.Tests;

public class ArtifactPathValidationTests
{
    [Theory]
    [InlineData("workflows/order.json", "workflows/order.json")]
    [InlineData("recipes\\init.yaml", "recipes/init.yaml")]
    public void NormalizesValidRelativePath(string path, string expected)
    {
        Assert.Equal(expected, DeploymentArtifactPathValidator.NormalizeRelativePath(path));
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
        Assert.Null(DeploymentArtifactPathValidator.NormalizeRelativePath(path));
    }
}
