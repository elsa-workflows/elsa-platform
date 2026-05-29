using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactRuntimeContractValidator(WorkflowArtifactRuntimeOptions options) : IWorkflowArtifactSchemaValidator
{
    private readonly WorkflowArtifactRuntimeCapability _capability = WorkflowArtifactRuntimeCapability.FromOptions(options);

    public WorkflowArtifactValidationResult Validate(ArtifactEnvelope envelope, WorkflowArtifactPayload payload) =>
        Validate(envelope, payload, _capability);

    public Task<WorkflowArtifactValidationResult> ValidateAsync(
        ArtifactEnvelope envelope,
        WorkflowArtifactPayload payload,
        WorkflowArtifactRuntimeCapability capability,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(envelope, payload, capability));

    private WorkflowArtifactValidationResult Validate(
        ArtifactEnvelope envelope,
        WorkflowArtifactPayload payload,
        WorkflowArtifactRuntimeCapability capability)
    {
        if (!envelope.ArtifactTypeId.Equals(ArtifactTypeIds.ElsaWorkflowDefinition, StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.UnsupportedArtifactType,
                Error("workflow-artifact.unsupported-type", "Workflow runtime only supports Elsa workflow definition artifacts."));
        }

        if (!capability.SupportedSchemaVersions.Contains(envelope.ArtifactSchemaVersion, StringComparer.OrdinalIgnoreCase))
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.UnsupportedSchema,
                Error("workflow-artifact.unsupported-schema", "Workflow artifact schema version is not supported by this runtime."));
        }

        if (!capability.Supports(envelope))
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.MissingCapability,
                Error("workflow-artifact.missing-capability", "Workflow artifact requires capabilities not advertised by this runtime."));
        }

        if (payload.SizeBytes > options.MaxPayloadBytes)
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.PayloadTooLarge,
                Error("workflow-artifact.payload-too-large", "Workflow artifact payload exceeds the configured runtime size limit."));
        }

        var observedDigest = ComputeDigest(payload.Content);
        if (observedDigest != envelope.ContentDigest)
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.DigestMismatch,
                Error("workflow-artifact.digest-mismatch", "Workflow artifact payload digest does not match the submitted artifact digest."),
                observedDigest);
        }

        if (!IsJson(payload.Content))
        {
            return WorkflowArtifactValidationResult.Invalid(
                WorkflowArtifactValidationStatus.InvalidPayload,
                Error("workflow-artifact.invalid-payload", "Workflow artifact payload is not valid JSON."),
                observedDigest);
        }

        return WorkflowArtifactValidationResult.Valid(observedDigest);
    }

    public static WorkflowArtifactDiagnostic SafeDiagnostic(
        string code,
        WorkflowArtifactDiagnosticSeverity severity,
        string message) =>
        new(code, severity, WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(message));

    public static ArtifactDigest ComputeDigest(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return new ArtifactDigest("sha256", Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static bool IsJson(byte[] payload)
    {
        try
        {
            using var _ = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static WorkflowArtifactDiagnostic Error(string code, string message) =>
        SafeDiagnostic(code, WorkflowArtifactDiagnosticSeverity.Error, message);
}
