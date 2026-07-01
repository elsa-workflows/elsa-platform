using Elsa.Platform.Deployment.Abstractions.Artifacts;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Artifacts.Tests;

public sealed class ArtifactEnvelopeValidationTests
{
    private readonly ArtifactTypeRegistry _types = new();
    private readonly ArtifactEnvelopeValidator _validator;

    public ArtifactEnvelopeValidationTests()
    {
        _validator = new ArtifactEnvelopeValidator(_types);
    }

    [Fact]
    public void Built_in_type_registry_contains_workflow_definition_type()
    {
        var type = _types.FindType(ArtifactTypeIds.ElsaWorkflowDefinition);

        type.Should().NotBeNull();
        type!.Enabled.Should().BeTrue();
        type.DefaultRuntimeFamily.Should().Be("elsa-workflows");
        type.DefaultRequiredCapabilities.Should().Contain(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition));
    }

    [Fact]
    public void Built_in_type_registry_contains_loom_recipe_type()
    {
        var type = _types.FindType(ArtifactTypeIds.ElsaLoomRecipe);

        type.Should().NotBeNull();
        type!.Enabled.Should().BeTrue();
        type.DefaultRuntimeFamily.Should().Be("elsa-workflows");
        type.DefaultRequiredCapabilities.Should().Contain(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaLoomRecipe));
    }

    [Fact]
    public void Validates_workflow_definition_envelope()
    {
        var act = () => _validator.Validate(Envelope());

        act.Should().NotThrow();
    }

    [Fact]
    public void Rejects_unknown_artifact_type()
    {
        var act = () => _validator.Validate(Envelope() with { ArtifactTypeId = "unknown.type" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Artifact type is not supported.");
    }

    [Theory]
    [InlineData("sha1", "abc123")]
    [InlineData("sha256", "x")]
    [InlineData("sha256", "contains space")]
    public void Rejects_invalid_digest_shape(string algorithm, string value)
    {
        var act = () => _validator.Validate(Envelope() with { ContentDigest = new ArtifactDigest(algorithm, value) });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_secret_like_display_metadata()
    {
        var envelope = Envelope() with
        {
            DisplayMetadata = new ArtifactDisplayMetadata(
                "Claims",
                "1.0.0",
                null,
                new Dictionary<string, string> { ["token"] = "value" },
                new Dictionary<string, string>())
        };

        var act = () => _validator.Validate(envelope);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Artifact metadata contains unsafe secret-like content.");
    }

    [Fact]
    public void Rejects_compatibility_hint_that_targets_different_type()
    {
        var envelope = Envelope() with
        {
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    "other.type",
                    "elsa-workflows",
                    null,
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ]
        };

        var act = () => _validator.Validate(envelope);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Artifact compatibility hint does not match the artifact type.");
    }

    private static ArtifactEnvelope Envelope() =>
        new(
            "sha256:claims-prod",
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaWorkflowDefinition,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            new ArtifactDigest("sha256", "claims-prod"),
            new ArtifactDigest("sha256", "claims-manifest"),
            new ArtifactPayloadReference("producer-managed", "studio://workflows/claims"),
            new ArtifactProducer("studio", "Elsa Studio", "4.0.0", "workflow:claims"),
            new ArtifactDisplayMetadata(
                "Claims",
                "1.0.0",
                "Claims workflow",
                new Dictionary<string, string> { ["domain"] = "claims" },
                new Dictionary<string, string>()),
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    ">=4.0.0",
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ],
            []);
}
