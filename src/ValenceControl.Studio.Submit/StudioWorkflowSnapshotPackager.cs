using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Artifacts;

namespace ValenceControl.Studio.Submit;

public sealed partial class StudioWorkflowSnapshotPackager(
    ArtifactEnvelopeValidator? validator = null) : IStudioWorkflowSnapshotPackager
{
    private readonly ArtifactEnvelopeValidator _validator = validator ?? new ArtifactEnvelopeValidator(new ArtifactTypeRegistry());

    public StudioSubmitPackage Package(
        WorkflowSubmissionSnapshot snapshot,
        StudioSubmitOptions options,
        DateTimeOffset? packagedAt = null)
    {
        ValidateSnapshot(snapshot);
        ValidateOptions(options);

        var recipeJson = BuildRecipeJson(snapshot);
        var contentDigest = ComputeDigest(recipeJson);
        var artifactId = BuildArtifactId(snapshot.WorkflowDefinitionId, contentDigest.Value);
        var payloadReference = new ArtifactPayloadReference(
            options.PayloadProvider.Trim(),
            BuildPayloadUri(options.PayloadUriScheme, snapshot.WorkflowDefinitionId, contentDigest.Value),
            "application/vnd.elsa.loom.recipe+json",
            Encoding.UTF8.GetByteCount(recipeJson),
            contentDigest);

        var producer = new ArtifactProducer(
            "studio",
            options.ProducerName.Trim(),
            TrimToNull(options.ProducerVersion),
            BuildSourceReference(snapshot));
        var labels = CopyMetadata(snapshot.Labels);
        var annotations = CopyMetadata(snapshot.Annotations);

        var displayMetadata = new ArtifactDisplayMetadata(
            snapshot.Name.Trim(),
            TrimToNull(snapshot.Version),
            TrimToNull(snapshot.Description),
            labels,
            annotations,
            TrimToNull(snapshot.SourceReference));

        var hints = new[]
        {
            new ArtifactCompatibilityHint(
                ArtifactTypeIds.ElsaLoomRecipe,
                "elsa-workflows",
                TrimToNull(options.RuntimeVersionRange),
                options.RequiredCapabilities.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                new Dictionary<string, string>())
        };

        var envelope = new ArtifactEnvelope(
            artifactId,
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaLoomRecipe,
            string.IsNullOrWhiteSpace(snapshot.WorkflowSchemaVersion)
                ? ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion
                : snapshot.WorkflowSchemaVersion.Trim(),
            contentDigest,
            null,
            payloadReference,
            producer,
            displayMetadata,
            hints,
            []);

        _validator.Validate(envelope);
        return new StudioSubmitPackage(envelope, recipeJson, (packagedAt ?? DateTimeOffset.UtcNow).ToUniversalTime());
    }

    private static void ValidateSnapshot(WorkflowSubmissionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.WorkflowDefinitionId))
            throw new InvalidOperationException("Workflow definition identity is required before submitting to Control.");
        if (string.IsNullOrWhiteSpace(snapshot.Name))
            throw new InvalidOperationException("Workflow display name is required before submitting to Control.");
        if (string.IsNullOrWhiteSpace(snapshot.WorkflowDefinitionJson))
            throw new InvalidOperationException("Workflow definition snapshot is required before submitting to Control.");
    }

    private static void ValidateOptions(StudioSubmitOptions options)
    {
        if (options.ControlEndpoint is null)
            throw new InvalidOperationException("Control endpoint is required before submitting to Control.");
        if (options.WorkspaceId is null || options.WorkspaceId == Guid.Empty)
            throw new InvalidOperationException("Control workspace is required before submitting to Control.");
        if (options.AuthenticationMode == StudioSubmitAuthenticationMode.ProviderCredentialReference && string.IsNullOrWhiteSpace(options.CredentialReference))
            throw new InvalidOperationException("Control credential reference is required for provider-backed Studio submission.");
        if (string.IsNullOrWhiteSpace(options.ProducerName))
            throw new InvalidOperationException("Studio producer name is required before submitting to Control.");
        if (string.IsNullOrWhiteSpace(options.PayloadProvider))
            throw new InvalidOperationException("Artifact payload provider is required before submitting to Control.");
        if (string.IsNullOrWhiteSpace(options.PayloadUriScheme))
            throw new InvalidOperationException("Artifact payload URI scheme is required before submitting to Control.");
    }

    private static ArtifactDigest ComputeDigest(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return new ArtifactDigest("sha256", Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string BuildRecipeJson(WorkflowSubmissionSnapshot snapshot)
    {
        var workflowDefinition = JsonNode.Parse(snapshot.WorkflowDefinitionJson)
            ?? throw new InvalidOperationException("Workflow definition snapshot is not valid JSON.");
        var recipe = new JsonObject
        {
            [LoomRecipeContract.SchemaVersionProperty] = LoomRecipeContract.SchemaVersion,
            ["id"] = snapshot.WorkflowDefinitionId.Trim(),
            ["name"] = snapshot.Name.Trim(),
            ["version"] = TrimToNull(snapshot.Version),
            [LoomRecipeContract.StepsProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = $"upsert-{SafeIdentitySegment(snapshot.WorkflowDefinitionId)}",
                    [LoomRecipeContract.StepTypeProperty] = LoomRecipeContract.WorkflowDefinitionUpsertStep,
                    ["publish"] = true,
                    [LoomRecipeContract.StepPayloadProperty] = workflowDefinition
                }
            }
        };

        return recipe.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string BuildArtifactId(string workflowDefinitionId, string digest) =>
        $"elsa.loom.recipe:{SafeIdentitySegment(workflowDefinitionId)}:{digest}";

    private static string BuildPayloadUri(string scheme, string workflowDefinitionId, string digest) =>
        $"{scheme.Trim()}://loom-recipes/{Uri.EscapeDataString(workflowDefinitionId.Trim())}/snapshots/{digest}";

    private static string BuildSourceReference(WorkflowSubmissionSnapshot snapshot)
    {
        var workflowId = snapshot.WorkflowDefinitionId.Trim();
        var versionId = TrimToNull(snapshot.WorkflowDefinitionVersionId);
        return versionId is null ? $"workflow:{workflowId}" : $"workflow:{workflowId}:version:{versionId}";
    }

    private static string SafeIdentitySegment(string value)
    {
        var safe = UnsafeIdentityCharacters().Replace(value.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safe))
            throw new InvalidOperationException("Workflow definition identity does not contain a safe artifact identity segment.");
        if (safe.Length <= 96)
            return safe;

        var identityDigest = ComputeDigest(value.Trim()).Value[..16];
        return $"{safe[..79]}-{identityDigest}";
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

    [GeneratedRegex("[^A-Za-z0-9_.-]+")]
    private static partial Regex UnsafeIdentityCharacters();
}
