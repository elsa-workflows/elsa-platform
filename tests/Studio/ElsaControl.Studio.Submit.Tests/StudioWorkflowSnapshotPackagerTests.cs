using ElsaControl.Deployment.Artifacts;
using ElsaControl.Studio.Submit;

namespace ElsaControl.Studio.Submit.Tests;

public sealed class StudioWorkflowSnapshotPackagerTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        ProducerVersion = "4.0.0",
        RuntimeVersionRange = ">=4.0.0"
    };

    [Fact]
    public void Packages_workflow_snapshot_as_elsa_loom_recipe_artifact_envelope()
    {
        var packagedAt = DateTimeOffset.Parse("2026-05-29T08:00:00Z");

        var package = _packager.Package(Snapshot(), _options, packagedAt);

        Assert.Equal(packagedAt, package.PackagedAt);
        Assert.Contains("\"PaymentRetry\"", package.WorkflowDefinitionJson);
        Assert.StartsWith("elsa.loom.recipe:payment-retry:", package.Envelope.ArtifactId);
        Assert.Equal(ArtifactTypeIds.ElsaLoomRecipe, package.Envelope.ArtifactTypeId);
        Assert.Equal(ArtifactEnvelopeConstants.EnvelopeVersion, package.Envelope.EnvelopeVersion);
        Assert.Equal(ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion, package.Envelope.ArtifactSchemaVersion);
        Assert.Equal("sha256", package.Envelope.ContentDigest.Algorithm);
        Assert.Equal("producer-managed", package.Envelope.PayloadReference.Provider);
        Assert.StartsWith("studio://loom-recipes/payment-retry/snapshots/", package.Envelope.PayloadReference.Uri);
        Assert.Equal(package.Envelope.ContentDigest, package.Envelope.PayloadReference.ReferenceDigest);
        Assert.Equal("studio", package.Envelope.Producer.ProducerType);
        Assert.Equal("Elsa Studio", package.Envelope.Producer.ProducerName);
        Assert.Equal("4.0.0", package.Envelope.Producer.ProducerVersion);
        Assert.Equal("workflow:payment-retry:version:v42", package.Envelope.Producer.SourceReference);
        Assert.Equal("Payment Retry", package.Envelope.DisplayMetadata.Name);
        Assert.Contains(new KeyValuePair<string, string>("domain", "payments"), package.Envelope.DisplayMetadata.Labels);
        Assert.Single(package.Envelope.CompatibilityHints);
        Assert.Contains("loom.recipe.apply", package.Envelope.CompatibilityHints.Single().RequiredCapabilities);
        Assert.Contains("\"schemaVersion\"", package.WorkflowDefinitionJson);
        Assert.Contains("\"workflowDefinition.upsert\"", package.WorkflowDefinitionJson);
    }

    [Fact]
    public void Uses_stable_artifact_identity_for_duplicate_snapshot()
    {
        var first = _packager.Package(Snapshot(), _options);
        var second = _packager.Package(Snapshot(), _options);

        Assert.Equal(first.Envelope.ArtifactId, second.Envelope.ArtifactId);
        Assert.Equal(first.Envelope.ContentDigest, second.Envelope.ContentDigest);
        Assert.Equal(first.Envelope.PayloadReference.Uri, second.Envelope.PayloadReference.Uri);
    }

    [Fact]
    public void Creates_different_identity_when_snapshot_content_changes()
    {
        var first = _packager.Package(Snapshot(), _options);
        var changed = _packager.Package(Snapshot(definitionJson: """{"id":"payment-retry","name":"PaymentRetry","version":43}"""), _options);

        Assert.NotEqual(first.Envelope.ArtifactId, changed.Envelope.ArtifactId);
        Assert.NotEqual(first.Envelope.ContentDigest, changed.Envelope.ContentDigest);
    }

    [Fact]
    public void Preserves_long_workflow_identity_uniqueness_in_artifact_id()
    {
        var sharedPrefix = new string('a', 120);
        var snapshotA = Snapshot(workflowDefinitionId: $"{sharedPrefix}-a");
        var snapshotB = Snapshot(workflowDefinitionId: $"{sharedPrefix}-b");

        var packageA = _packager.Package(snapshotA, _options);
        var packageB = _packager.Package(snapshotB, _options);

        Assert.NotEqual(packageB.Envelope.ContentDigest, packageA.Envelope.ContentDigest);
        Assert.NotEqual(packageB.Envelope.ArtifactId, packageA.Envelope.ArtifactId);
        Assert.True(packageA.Envelope.ArtifactId.Length <= 256);
        Assert.True(packageB.Envelope.ArtifactId.Length <= 256);
    }

    [Fact]
    public void Rejects_unsafe_metadata_before_submission()
    {
        var snapshot = Snapshot(labels: new Dictionary<string, string> { ["token"] = "abc" });

        var act = () => _packager.Package(snapshot, _options);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Artifact metadata contains unsafe secret-like content.", exception.Message);
    }

    [Fact]
    public void Copies_metadata_before_validation_boundary()
    {
        var labels = new Dictionary<string, string> { ["domain"] = "payments" };
        var package = _packager.Package(Snapshot(labels: labels), _options);

        labels["token"] = "abc";

        Assert.Contains(new KeyValuePair<string, string>("domain", "payments"), package.Envelope.DisplayMetadata.Labels);
        Assert.DoesNotContain("token", package.Envelope.DisplayMetadata.Labels.Keys);
    }

    [Fact]
    public void Requires_control_endpoint_and_workspace_configuration()
    {
        var act = () => _packager.Package(Snapshot(), _options with { ControlEndpoint = null });

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Control endpoint is required before submitting to Control.", exception.Message);
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

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Control credential reference is required for provider-backed Studio submission.", exception.Message);
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
