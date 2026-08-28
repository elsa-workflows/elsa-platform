using System.Text;
using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Artifacts;
using ElsaControl.Workflows.RuntimeApplier;

namespace ElsaControl.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactRuntimeContractTests
{
    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        WorkerId = "worker-a",
        RuntimeVersion = "4.0.0"
    };

    [Fact]
    public void Advertises_workflow_artifact_capability_from_options()
    {
        var capability = WorkflowArtifactRuntimeCapability.FromOptions(_options);

        Assert.Equal(ArtifactTypeIds.ElsaWorkflowDefinition, capability.ArtifactTypeId);
        Assert.Equal("elsa-workflows", capability.RuntimeFamily);
        Assert.Equal("4.0.0", capability.RuntimeVersion);
        Assert.Contains(ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion, capability.SupportedSchemaVersions);
        Assert.Contains("workflow-definition.apply", capability.Capabilities);
        Assert.True(capability.Supports(Envelope(Payload())));
    }

    [Fact]
    public void Requires_runtime_version_before_advertising_artifact_capabilities()
    {
        var act = () => WorkflowArtifactRuntimeCapability.FromOptions(_options with { RuntimeVersion = null });

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime version is required before advertising artifact capabilities.", exception.Message);
    }

    [Fact]
    public void Requires_control_endpoint_before_sync()
    {
        var act = () => (_options with { ControlEndpoint = null }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Control endpoint is required before runtime command sync can start.", exception.Message);
    }

    [Fact]
    public void Rejects_blank_capability_advertisement()
    {
        var act = () => (_options with { Capabilities = [" "] }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("At least one runtime capability must be advertised.", exception.Message);
    }

    [Fact]
    public void Rejects_null_capability_advertisement()
    {
        var act = () => (_options with { Capabilities = null! }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("At least one runtime capability must be advertised.", exception.Message);
    }

    [Fact]
    public void Rejects_lease_duration_that_cannot_be_sent_to_control()
    {
        var act = () => (_options with { ClaimLeaseDuration = TimeSpan.FromMilliseconds(500) }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command lease duration must be between 1 second and the maximum supported Control lease.", exception.Message);
    }

    [Fact]
    public void Rejects_retry_and_lease_policy_timing_that_cannot_guard_processing()
    {
        var act = () => (_options with { ClaimLeaseDuration = TimeSpan.FromSeconds(10), HeartbeatInterval = TimeSpan.FromSeconds(10) }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command heartbeat interval must be positive and shorter than the command lease duration.", exception.Message);
    }

    [Fact]
    public void Rejects_non_positive_payload_request_timeout()
    {
        var act = () => (_options with { PayloadRequestTimeout = TimeSpan.Zero }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload request timeout must be positive.", exception.Message);
    }

    [Fact]
    public void Rejects_payload_size_limit_that_cannot_be_buffered()
    {
        var act = () => (_options with { MaxPayloadBytes = (long)Array.MaxLength + 1 }).Validate();

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Maximum workflow artifact payload size must be between 1 byte and the maximum supported runtime buffer.", exception.Message);
    }

    [Fact]
    public void Validates_payload_digest_and_schema_contracts()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.Valid, result.Status);
        Assert.Equal(envelope.ContentDigest, result.ObservedDigest);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validates_payload_digest_case_insensitively()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var uppercaseDigest = new ArtifactDigest(
            envelope.ContentDigest.Algorithm.ToUpperInvariant(),
            envelope.ContentDigest.Value.ToUpperInvariant());
        envelope = envelope with
        {
            ContentDigest = uppercaseDigest,
            PayloadReference = envelope.PayloadReference with { ReferenceDigest = uppercaseDigest }
        };
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.Valid, result.Status);
        Assert.Equal(WorkflowArtifactRuntimeContractValidator.ComputeDigest(payload), result.ObservedDigest);
    }

    [Fact]
    public void Rejects_digest_mismatch_without_exposing_payload()
    {
        var payload = Payload("""{"id":"payment-retry","version":43}""");
        var envelope = Envelope(Payload());
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.DigestMismatch, result.Status);
        Assert.False(result.Succeeded);
        Assert.Single(result.Diagnostics, x => x.Code == "workflow-artifact.digest-mismatch");
        Assert.DoesNotContain("payment-retry", result.Diagnostics.Single().Message);
        Assert.NotEqual(envelope.ContentDigest, result.ObservedDigest);
    }

    [Fact]
    public void Rejects_payload_reference_digest_mismatch_without_exposing_payload()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        envelope = envelope with
        {
            PayloadReference = envelope.PayloadReference with
            {
                ReferenceDigest = new ArtifactDigest("sha256", new string('b', 64))
            }
        };
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.DigestMismatch, result.Status);
        Assert.False(result.Succeeded);
        Assert.Single(result.Diagnostics, x => x.Code == "workflow-artifact.reference-digest-mismatch");
        Assert.DoesNotContain("payment-retry", result.Diagnostics.Single().Message);
        Assert.Equal(WorkflowArtifactRuntimeContractValidator.ComputeDigest(payload), result.ObservedDigest);
    }

    [Fact]
    public void Rejects_unsupported_schema_and_missing_capability()
    {
        var payload = Payload();
        var unsupportedSchema = Envelope(payload) with { ArtifactSchemaVersion = "9.0" };
        var missingCapability = Envelope(payload) with
        {
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    ">=4.0.0",
                    ["workflow-definition.apply", "workflow-definition.delete"],
                    new Dictionary<string, string>())
            ]
        };
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        Assert.Equal(
            WorkflowArtifactValidationStatus.UnsupportedSchema,
            validator.Validate(unsupportedSchema, PayloadResult(payload, unsupportedSchema.PayloadReference)).Status);
        Assert.Equal(
            WorkflowArtifactValidationStatus.MissingCapability,
            validator.Validate(missingCapability, PayloadResult(payload, missingCapability.PayloadReference)).Status);
    }

    [Theory]
    [InlineData(">=4.0.0 <5.0.0", WorkflowArtifactValidationStatus.Valid)]
    [InlineData("[4.0.0,5.0.0)", WorkflowArtifactValidationStatus.Valid)]
    [InlineData(">4.0.0", WorkflowArtifactValidationStatus.MissingCapability)]
    [InlineData(">=5.0.0", WorkflowArtifactValidationStatus.MissingCapability)]
    [InlineData("not-a-range", WorkflowArtifactValidationStatus.MissingCapability)]
    public void Evaluates_runtime_version_ranges_when_matching_compatibility_hints(
        string versionRange,
        WorkflowArtifactValidationStatus expectedStatus)
    {
        var payload = Payload();
        var envelope = Envelope(payload) with
        {
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    versionRange,
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ]
        };
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        Assert.Equal(expectedStatus, validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference)).Status);
    }

    [Fact]
    public void Evaluates_runtime_version_ranges_with_build_metadata()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options with { RuntimeVersion = "4.0.0+build.1" });

        Assert.Equal(WorkflowArtifactValidationStatus.Valid, validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference)).Status);
    }

    [Theory]
    [InlineData("4.0.0-preview.1", ">=4.0.0")]
    [InlineData("4.0.0-preview.1", "4.0.0")]
    [InlineData("4.0.0", ">=4..0")]
    public void Rejects_prerelease_or_malformed_runtime_version_ranges(string runtimeVersion, string versionRange)
    {
        var payload = Payload();
        var envelope = Envelope(payload) with
        {
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    versionRange,
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ]
        };
        var validator = new WorkflowArtifactRuntimeContractValidator(_options with { RuntimeVersion = runtimeVersion });

        Assert.Equal(WorkflowArtifactValidationStatus.MissingCapability, validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference)).Status);
    }

    [Fact]
    public void Rejects_invalid_json_payload_even_when_digest_matches()
    {
        var payload = Payload("""{"id":""");
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.InvalidPayload, result.Status);
        Assert.Equal(envelope.ContentDigest, result.ObservedDigest);
    }

    [Fact]
    public void Rejects_non_object_json_payload_even_when_digest_matches()
    {
        var payload = Payload("""["payment-retry"]""");
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.InvalidPayload, result.Status);
        Assert.Equal(envelope.ContentDigest, result.ObservedDigest);
    }

    [Fact]
    public void Sanitizes_runtime_diagnostics()
    {
        var diagnostic = WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(
            "workflow-artifact.local-validation-failed",
            WorkflowArtifactDiagnosticSeverity.Error,
            "Bearer token and password appeared in runtime exception");

        Assert.DoesNotContain("Bearer", diagnostic.Message);
        Assert.DoesNotContain("password", diagnostic.Message);
        Assert.Contains("[redacted]", diagnostic.Message);
    }

    [Fact]
    public void Defines_safe_apply_result_success_states()
    {
        var digest = new ArtifactDigest("sha256", new string('a', 64));
        var applied = new WorkflowArtifactApplyResult(
            WorkflowArtifactApplyStatus.Applied,
            digest,
            "workflow:payment-retry:42",
            []);
        var alreadyApplied = applied with { Status = WorkflowArtifactApplyStatus.AlreadyApplied };
        var rejected = applied with { Status = WorkflowArtifactApplyStatus.Rejected };

        Assert.True(applied.Succeeded);
        Assert.True(alreadyApplied.Succeeded);
        Assert.False(rejected.Succeeded);
    }

    [Fact]
    public void Validates_loom_recipe_artifacts_with_default_capabilities()
    {
        var payload = Payload();
        var envelope = LoomRecipeEnvelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.Valid, result.Status);
    }

    [Fact]
    public void Rejects_loom_recipe_artifacts_when_loom_capability_is_not_advertised()
    {
        var payload = Payload();
        var envelope = LoomRecipeEnvelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options with { Capabilities = ["workflow-definition.apply"] });

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        Assert.Equal(WorkflowArtifactValidationStatus.UnsupportedArtifactType, result.Status);
    }

    private static byte[] Payload(string json = """{"id":"payment-retry","version":42}""") =>
        Encoding.UTF8.GetBytes(json);

    private static WorkflowArtifactPayload PayloadResult(byte[] payload, ArtifactPayloadReference reference) =>
        new(reference, payload, "application/vnd.elsa.workflow-definition+json");

    private static ArtifactEnvelope LoomRecipeEnvelope(byte[] payload) =>
        Envelope(payload) with
        {
            ArtifactTypeId = ArtifactTypeIds.ElsaLoomRecipe,
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaLoomRecipe,
                    "elsa-workflows",
                    ">=4.0.0",
                    [ArtifactApplyCapability.For(ArtifactTypeIds.ElsaLoomRecipe)],
                    new Dictionary<string, string>())
            ]
        };

    private static ArtifactEnvelope Envelope(byte[] payload)
    {
        var digest = WorkflowArtifactRuntimeContractValidator.ComputeDigest(payload);
        return new ArtifactEnvelope(
            $"elsa.workflow-definition:payment-retry:{digest.Value}",
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaWorkflowDefinition,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            digest,
            null,
            new ArtifactPayloadReference(
                "producer-managed",
                $"studio://workflows/payment-retry/snapshots/{digest.Value}",
                "application/vnd.elsa.workflow-definition+json",
                payload.Length,
                digest),
            new ArtifactProducer("studio", "Elsa Studio", "4.0.0", "workflow:payment-retry:version:v42"),
            new ArtifactDisplayMetadata(
                "Payment Retry",
                "42",
                "Retries payment collection failures.",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                "studio://workflows/payment-retry"),
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
}
