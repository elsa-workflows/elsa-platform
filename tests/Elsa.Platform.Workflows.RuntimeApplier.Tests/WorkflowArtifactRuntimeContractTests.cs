using System.Text;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;

namespace Elsa.Platform.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactRuntimeContractTests
{
    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        WorkerId = "worker-a",
        RuntimeVersion = "4.0.0"
    };

    [Fact]
    public void Advertises_workflow_artifact_capability_from_options()
    {
        var capability = WorkflowArtifactRuntimeCapability.FromOptions(_options);

        capability.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        capability.RuntimeFamily.Should().Be("elsa-workflows");
        capability.RuntimeVersion.Should().Be("4.0.0");
        capability.SupportedSchemaVersions.Should().Contain(ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion);
        capability.Capabilities.Should().Contain("workflow-definition.apply");
        capability.Supports(Envelope(Payload())).Should().BeTrue();
    }

    [Fact]
    public void Requires_platform_endpoint_before_sync()
    {
        var act = () => (_options with { PlatformEndpoint = null }).Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Platform endpoint is required before runtime command sync can start.");
    }

    [Fact]
    public void Rejects_blank_capability_advertisement()
    {
        var act = () => (_options with { Capabilities = [" "] }).Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("At least one runtime capability must be advertised.");
    }

    [Fact]
    public void Rejects_null_capability_advertisement()
    {
        var act = () => (_options with { Capabilities = null! }).Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("At least one runtime capability must be advertised.");
    }

    [Fact]
    public void Validates_payload_digest_and_schema_contracts()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        result.Status.Should().Be(WorkflowArtifactValidationStatus.Valid);
        result.ObservedDigest.Should().Be(envelope.ContentDigest);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Rejects_digest_mismatch_without_exposing_payload()
    {
        var payload = Payload("""{"id":"payment-retry","version":43}""");
        var envelope = Envelope(Payload());
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        result.Status.Should().Be(WorkflowArtifactValidationStatus.DigestMismatch);
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(x => x.Code == "workflow-artifact.digest-mismatch");
        result.Diagnostics.Single().Message.Should().NotContain("payment-retry");
        result.ObservedDigest.Should().NotBe(envelope.ContentDigest);
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

        validator.Validate(unsupportedSchema, PayloadResult(payload, unsupportedSchema.PayloadReference))
            .Status.Should().Be(WorkflowArtifactValidationStatus.UnsupportedSchema);
        validator.Validate(missingCapability, PayloadResult(payload, missingCapability.PayloadReference))
            .Status.Should().Be(WorkflowArtifactValidationStatus.MissingCapability);
    }

    [Fact]
    public void Rejects_invalid_json_payload_even_when_digest_matches()
    {
        var payload = Payload("""{"id":""");
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        result.Status.Should().Be(WorkflowArtifactValidationStatus.InvalidPayload);
        result.ObservedDigest.Should().Be(envelope.ContentDigest);
    }

    [Fact]
    public void Rejects_non_object_json_payload_even_when_digest_matches()
    {
        var payload = Payload("""["payment-retry"]""");
        var envelope = Envelope(payload);
        var validator = new WorkflowArtifactRuntimeContractValidator(_options);

        var result = validator.Validate(envelope, PayloadResult(payload, envelope.PayloadReference));

        result.Status.Should().Be(WorkflowArtifactValidationStatus.InvalidPayload);
        result.ObservedDigest.Should().Be(envelope.ContentDigest);
    }

    [Fact]
    public void Sanitizes_runtime_diagnostics()
    {
        var diagnostic = WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(
            "workflow-artifact.local-validation-failed",
            WorkflowArtifactDiagnosticSeverity.Error,
            "Bearer token and password appeared in runtime exception");

        diagnostic.Message.Should().NotContain("Bearer");
        diagnostic.Message.Should().NotContain("password");
        diagnostic.Message.Should().Contain("[redacted]");
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

        applied.Succeeded.Should().BeTrue();
        alreadyApplied.Succeeded.Should().BeTrue();
        rejected.Succeeded.Should().BeFalse();
    }

    private static byte[] Payload(string json = """{"id":"payment-retry","version":42}""") =>
        Encoding.UTF8.GetBytes(json);

    private static WorkflowArtifactPayload PayloadResult(byte[] payload, ArtifactPayloadReference reference) =>
        new(reference, payload, "application/vnd.elsa.workflow-definition+json");

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
