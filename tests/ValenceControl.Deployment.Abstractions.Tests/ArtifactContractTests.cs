using System.Text.Json;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;
using ValenceControl.Deployment.Abstractions.Targets;
using FluentAssertions;

namespace ValenceControl.Deployment.Abstractions.Tests;

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
    public void ArtifactDigestRoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(_manifestDigest);

        JsonSerializer.Deserialize<ArtifactDigest>(json).Should().Be(_manifestDigest);
    }

    [Fact]
    public void ArtifactIdentityCapturesRequiredDigestsAndSchemaVersion()
    {
        var identity = new DeploymentArtifactIdentity(
            "sales-staging",
            "valence-control/v1alpha1",
            _manifestDigest,
            _contentDigest,
            version: "2026.05.20.1");

        identity.Id.Should().Be("sales-staging");
        identity.Version.Should().Be("2026.05.20.1");
        identity.SchemaVersion.Should().Be("valence-control/v1alpha1");
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
        new DeploymentArtifactMetadata(identity, DateTimeOffset.UtcNow).Properties.Should().BeEmpty();
    }

    private static void AssertReadOnly(IReadOnlyDictionary<string, string> values)
    {
        var mutate = () => ((IDictionary<string, string>)values).Add("leaked", "true");

        mutate.Should().Throw<NotSupportedException>();
        values.Should().BeEmpty();
    }
}
