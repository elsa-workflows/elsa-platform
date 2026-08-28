using ElsaControl.Deployment.Abstractions.Artifacts;

namespace ElsaControl.Deployment.Artifacts.Tests;

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

        Assert.NotNull(type);
        Assert.True(type!.Enabled);
        Assert.Equal("elsa-workflows", type.DefaultRuntimeFamily);
        Assert.NotNull(type.DefaultRequiredCapabilities);
        Assert.Contains(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition), type.DefaultRequiredCapabilities!);
    }

    [Fact]
    public void Built_in_type_registry_contains_loom_recipe_type()
    {
        var type = _types.FindType(ArtifactTypeIds.ElsaLoomRecipe);

        Assert.NotNull(type);
        Assert.True(type!.Enabled);
        Assert.Equal("elsa-workflows", type.DefaultRuntimeFamily);
        Assert.NotNull(type.DefaultRequiredCapabilities);
        Assert.Contains(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaLoomRecipe), type.DefaultRequiredCapabilities!);
    }

    [Fact]
    public void Validates_workflow_definition_envelope()
    {
        var act = () => _validator.Validate(Envelope());

        Assert.Null(Record.Exception(act));
    }

    [Fact]
    public void Rejects_unknown_artifact_type()
    {
        var act = () => _validator.Validate(Envelope() with { ArtifactTypeId = "unknown.type" });

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Artifact type is not supported.", exception.Message);
    }

    [Theory]
    [InlineData("sha1", "abc123")]
    [InlineData("sha256", "x")]
    [InlineData("sha256", "contains space")]
    public void Rejects_invalid_digest_shape(string algorithm, string value)
    {
        var act = () => _validator.Validate(Envelope() with { ContentDigest = new ArtifactDigest(algorithm, value) });

        Assert.Throws<InvalidOperationException>(act);
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

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Artifact metadata contains unsafe secret-like content.", exception.Message);
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

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Artifact compatibility hint does not match the artifact type.", exception.Message);
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
