using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.Tests;

public class ArtifactContractTests
{
    private readonly ArtifactDigest _manifestDigest = new("sha256", "manifest");
    private readonly ArtifactDigest _contentDigest = new("sha256", "content");

    [Fact]
    public void ArtifactDigestRequiresAlgorithmAndValue()
    {
        var missingAlgorithm = () => new ArtifactDigest("", "content");
        var missingValue = () => new ArtifactDigest("sha256", " ");

        Assert.Throws<ArgumentException>(() => _ = missingAlgorithm());
        Assert.Throws<ArgumentException>(() => _ = missingValue());
    }

    [Fact]
    public void ArtifactDigestFormatsAlgorithmAndValue()
    {
        Assert.Equal("sha256:manifest", _manifestDigest.ToString());
    }

    [Fact]
    public void ArtifactDigestRoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(_manifestDigest);

        Assert.Equal(_manifestDigest, JsonSerializer.Deserialize<ArtifactDigest>(json));
    }

    [Fact]
    public void ArtifactIdentityCapturesRequiredDigestsAndSchemaVersion()
    {
        var identity = new DeploymentArtifactIdentity(
            "sales-staging",
            "elsa-control/v1alpha1",
            _manifestDigest,
            _contentDigest,
            version: "2026.05.20.1");

        Assert.Equal("sales-staging", identity.Id);
        Assert.Equal("2026.05.20.1", identity.Version);
        Assert.Equal("elsa-control/v1alpha1", identity.SchemaVersion);
        Assert.Equal(_manifestDigest, identity.ManifestDigest);
        Assert.Equal(_contentDigest, identity.ContentDigest);
    }

    [Fact]
    public void ArtifactMetadataNormalizesBuildTimestampToUtc()
    {
        var identity = new DeploymentArtifactIdentity("sales-staging", "v1alpha", _manifestDigest, _contentDigest);
        var localTimestamp = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.FromHours(2));

        var metadata = new DeploymentArtifactMetadata(identity, localTimestamp, builder: "cli", source: "abc123");

        Assert.Equal(identity, metadata.Identity);
        Assert.Equal(TimeSpan.Zero, metadata.BuiltAt.Offset);
        Assert.Equal("cli", metadata.Builder);
        Assert.Equal("abc123", metadata.Source);
    }

    [Fact]
    public void DefaultMetadataDictionariesCannotBeMutatedThroughDictionaryDowncast()
    {
        var identity = new DeploymentArtifactIdentity("sales-staging", "v1alpha", _manifestDigest, _contentDigest);
        var resource = new DeploymentResource(new DeploymentResourceId("variable", "orderTimeout"));
        var state = new DeploymentResourceState(resource.Id);
        var diagnostic = new DeploymentDiagnostic("test", DeploymentDiagnosticSeverity.Info, "Message");
        var target = new DeploymentTargetDescriptor("staging");

        AssertReadOnly(new DeploymentArtifactMetadata(identity, DateTimeOffset.UtcNow).Properties);
        AssertReadOnly(resource.Metadata);
        AssertReadOnly(state.Metadata);
        AssertReadOnly(diagnostic.Details);
        AssertReadOnly(target.Properties);
        Assert.Empty(new DeploymentArtifactMetadata(identity, DateTimeOffset.UtcNow).Properties);
    }

    private static void AssertReadOnly(IReadOnlyDictionary<string, string> values)
    {
        var mutate = () => ((IDictionary<string, string>)values).Add("leaked", "true");

        Assert.Throws<NotSupportedException>(mutate);
        Assert.Empty(values);
    }
}
