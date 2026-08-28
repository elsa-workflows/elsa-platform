
namespace ElsaControl.Deployment.Artifacts.Tests;

public class ArtifactChecksumTests
{
    [Fact]
    public void ContentDigestIsStableForEntryOrdering()
    {
        var first = new[]
        {
            new DeploymentArtifactChecksumEntry("payload/b.txt", DeploymentArtifactEntryKind.Payload, "sha256", "b", 1),
            new DeploymentArtifactChecksumEntry("payload/a.txt", DeploymentArtifactEntryKind.Payload, "sha256", "a", 1)
        };
        var second = first.Reverse();

        Assert.Equal(
            DeploymentArtifactChecksumService.ComputeContentDigest(second),
            DeploymentArtifactChecksumService.ComputeContentDigest(first));
    }
}
