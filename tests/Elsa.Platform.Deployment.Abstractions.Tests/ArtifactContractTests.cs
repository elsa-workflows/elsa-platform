using Elsa.Platform.Deployment.Abstractions.Artifacts;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Abstractions.Tests;

public class ArtifactContractTests
{
    private readonly ArtifactDigest _manifestDigest = new("sha256", "manifest");
    private readonly ArtifactDigest _contentDigest = new("sha256", "content");

    [Fact]
    public void ArtifactDigestRequiresAlgorithmAndValue()
    {
        var missingAlgorithm = () => new ArtifactDigest("", "content");
        var missingValue = () => new ArtifactDigest("sha256", " ");

        missingAlgorithm.Should().Throw<ArgumentException>();
        missingValue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ArtifactDigestFormatsAlgorithmAndValue()
    {
        _manifestDigest.ToString().Should().Be("sha256:manifest");
    }

    [Fact]
    public void ArtifactIdentityCapturesRequiredDigestsAndSchemaVersion()
    {
        var identity = new DeploymentArtifactIdentity(
            "sales-staging",
            "platform.elsa.io/v1alpha1",
            _manifestDigest,
            _contentDigest,
            version: "2026.05.20.1");

        identity.Id.Should().Be("sales-staging");
        identity.Version.Should().Be("2026.05.20.1");
        identity.SchemaVersion.Should().Be("platform.elsa.io/v1alpha1");
        identity.ManifestDigest.Should().Be(_manifestDigest);
        identity.ContentDigest.Should().Be(_contentDigest);
    }

    [Fact]
    public void ArtifactMetadataNormalizesBuildTimestampToUtc()
    {
        var identity = new DeploymentArtifactIdentity("sales-staging", "v1alpha", _manifestDigest, _contentDigest);
        var localTimestamp = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.FromHours(2));

        var metadata = new DeploymentArtifactMetadata(identity, localTimestamp, builder: "cli", source: "abc123");

        metadata.Identity.Should().Be(identity);
        metadata.BuiltAt.Offset.Should().Be(TimeSpan.Zero);
        metadata.Builder.Should().Be("cli");
        metadata.Source.Should().Be("abc123");
    }
}
