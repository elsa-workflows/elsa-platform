using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Studio.Submit;
using FluentAssertions;

namespace Elsa.Platform.Studio.Submit.Tests;

public sealed class StudioWorkflowSnapshotPackagerTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        ProducerVersion = "4.0.0",
        RuntimeVersionRange = ">=4.0.0"
    };

    [Fact]
    public void Packages_workflow_snapshot_as_elsa_loom_recipe_artifact_envelope()
    {
        var packagedAt = DateTimeOffset.Parse("2026-05-29T08:00:00Z");

        var package = _packager.Package(Snapshot(), _options, packagedAt);

        package.PackagedAt.Should().Be(packagedAt);
        package.WorkflowDefinitionJson.Should().Contain("\"PaymentRetry\"");
        package.Envelope.ArtifactId.Should().StartWith("elsa.loom.recipe:payment-retry:");
        package.Envelope.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaLoomRecipe);
        package.Envelope.EnvelopeVersion.Should().Be(ArtifactEnvelopeConstants.EnvelopeVersion);
        package.Envelope.ArtifactSchemaVersion.Should().Be(ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion);
        package.Envelope.ContentDigest.Algorithm.Should().Be("sha256");
        package.Envelope.PayloadReference.Provider.Should().Be("producer-managed");
        package.Envelope.PayloadReference.Uri.Should().StartWith("studio://loom-recipes/payment-retry/snapshots/");
        package.Envelope.PayloadReference.ReferenceDigest.Should().Be(package.Envelope.ContentDigest);
        package.Envelope.Producer.ProducerType.Should().Be("studio");
        package.Envelope.Producer.ProducerName.Should().Be("Elsa Studio");
        package.Envelope.Producer.ProducerVersion.Should().Be("4.0.0");
        package.Envelope.Producer.SourceReference.Should().Be("workflow:payment-retry:version:v42");
        package.Envelope.DisplayMetadata.Name.Should().Be("Payment Retry");
        package.Envelope.DisplayMetadata.Labels.Should().Contain("domain", "payments");
        package.Envelope.CompatibilityHints.Should().ContainSingle();
        package.Envelope.CompatibilityHints.Single().RequiredCapabilities.Should().Contain("loom.recipe.apply");
        package.WorkflowDefinitionJson.Should().Contain("\"schemaVersion\"");
        package.WorkflowDefinitionJson.Should().Contain("\"workflowDefinition.upsert\"");
    }

    [Fact]
    public void Uses_stable_artifact_identity_for_duplicate_snapshot()
    {
        var first = _packager.Package(Snapshot(), _options);
        var second = _packager.Package(Snapshot(), _options);

        second.Envelope.ArtifactId.Should().Be(first.Envelope.ArtifactId);
        second.Envelope.ContentDigest.Should().Be(first.Envelope.ContentDigest);
        second.Envelope.PayloadReference.Uri.Should().Be(first.Envelope.PayloadReference.Uri);
    }

    [Fact]
    public void Creates_different_identity_when_snapshot_content_changes()
    {
        var first = _packager.Package(Snapshot(), _options);
        var changed = _packager.Package(Snapshot(definitionJson: """{"id":"payment-retry","name":"PaymentRetry","version":43}"""), _options);

        changed.Envelope.ArtifactId.Should().NotBe(first.Envelope.ArtifactId);
        changed.Envelope.ContentDigest.Should().NotBe(first.Envelope.ContentDigest);
    }

    [Fact]
    public void Preserves_long_workflow_identity_uniqueness_in_artifact_id()
    {
        var sharedPrefix = new string('a', 120);
        var snapshotA = Snapshot(workflowDefinitionId: $"{sharedPrefix}-a");
        var snapshotB = Snapshot(workflowDefinitionId: $"{sharedPrefix}-b");

        var packageA = _packager.Package(snapshotA, _options);
        var packageB = _packager.Package(snapshotB, _options);

        packageA.Envelope.ContentDigest.Should().NotBe(packageB.Envelope.ContentDigest);
        packageA.Envelope.ArtifactId.Should().NotBe(packageB.Envelope.ArtifactId);
        packageA.Envelope.ArtifactId.Length.Should().BeLessThanOrEqualTo(256);
        packageB.Envelope.ArtifactId.Length.Should().BeLessThanOrEqualTo(256);
    }

    [Fact]
    public void Rejects_unsafe_metadata_before_submission()
    {
        var snapshot = Snapshot(labels: new Dictionary<string, string> { ["token"] = "abc" });

        var act = () => _packager.Package(snapshot, _options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Artifact metadata contains unsafe secret-like content.");
    }

    [Fact]
    public void Copies_metadata_before_validation_boundary()
    {
        var labels = new Dictionary<string, string> { ["domain"] = "payments" };
        var package = _packager.Package(Snapshot(labels: labels), _options);

        labels["token"] = "abc";

        package.Envelope.DisplayMetadata.Labels.Should().Contain("domain", "payments");
        package.Envelope.DisplayMetadata.Labels.Should().NotContainKey("token");
    }

    [Fact]
    public void Requires_platform_endpoint_and_workspace_configuration()
    {
        var act = () => _packager.Package(Snapshot(), _options with { PlatformEndpoint = null });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Platform endpoint is required before submitting to Platform.");
    }

    [Fact]
    public void Requires_provider_credential_reference_when_configured()
    {
        var options = _options with
        {
            AuthenticationMode = StudioSubmitAuthenticationMode.ProviderCredentialReference,
            CredentialReference = null
        };

        var act = () => _packager.Package(Snapshot(), options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Platform credential reference is required for provider-backed Studio submission.");
    }

    private static WorkflowSubmissionSnapshot Snapshot(
        string workflowDefinitionId = "payment-retry",
        string definitionJson = """{"id":"payment-retry","name":"PaymentRetry","version":42}""",
        IReadOnlyDictionary<string, string>? labels = null) =>
        new(
            workflowDefinitionId,
            "v42",
            "Payment Retry",
            "42",
            "Retries payment collection failures.",
            definitionJson,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            "studio://workflows/payment-retry",
            labels ?? new Dictionary<string, string> { ["domain"] = "payments" },
            new Dictionary<string, string> { ["owner"] = "finance-ops" });
}
