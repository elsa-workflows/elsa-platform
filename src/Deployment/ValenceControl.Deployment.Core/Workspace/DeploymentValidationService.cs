using System.Text.Json;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Cockpit;

namespace ValenceControl.Deployment.Core.Workspace;

public sealed class DeploymentValidationService(IWorkspaceDeploymentStore? store = null)
{
    public async Task<PromotionComparison> PreviewPromotionAsync(
        Guid workspaceId,
        WorkspacePromotionPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Promotion preview requires a workspace deployment store.");

        var source = await store.GetRevisionAsync(workspaceId, request.SourceRevisionId, cancellationToken);
        var target = await store.GetLatestRevisionAsync(workspaceId, request.TargetEnvironmentId, cancellationToken);
        var engine = await store.GetEngineAsync(workspaceId, request.TargetEngineId, cancellationToken);
        if (source is null)
            return Blocked(request, "deployment.preview.source-missing", "Source revision is not visible in this workspace.");

        var sourceRecords = ParseRecords(source.DesiredStateJson);
        var targetRecords = target is null ? [] : ParseRecords(target.DesiredStateJson);
        var (sourceEnvironment, targetEnvironment, targetEngineRegistration) = await GetPromotionContextAsync(workspaceId, request, cancellationToken);
        var diff = Diff(sourceRecords, targetRecords);
        var artifactComparisons = CompareArtifacts(sourceRecords, targetRecords, engine, targetEngineRegistration);
        var validations = Validate(sourceRecords, engine, sourceEnvironment, targetEnvironment, request.TargetEnvironmentId);

        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            source.Id.ToString("D"),
            source.RevisionNumber,
            target?.RevisionNumber ?? 0,
            diff,
            validations,
            target?.RevisionNumber,
            target?.Id.ToString("D"))
        {
            Artifacts = artifactComparisons
        };
    }

    public PromotionComparison PreviewPromotion(WorkspacePromotionPreviewRequest request)
    {
        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            request.SourceRevisionId.ToString("D"),
            0,
            0,
            [],
            [new DeploymentValidation("deployment.preview.not-implemented", ValidationSeverity.Blocker, "Deployment preview", "Promotion preview is not implemented yet.")],
            null,
            null);
    }

    private static PromotionComparison Blocked(WorkspacePromotionPreviewRequest request, string id, string message) =>
        new(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            request.SourceRevisionId.ToString("D"),
            0,
            0,
            [],
            [new DeploymentValidation(id, ValidationSeverity.Blocker, "Deployment preview", message)],
            null,
            null);

    private static IReadOnlyList<DesiredRecord> ParseRecords(string json)
    {
        using var document = JsonDocument.Parse(json);
        var records = document.RootElement.TryGetProperty("records", out var recordsElement) && recordsElement.ValueKind == JsonValueKind.Array
            ? recordsElement
            : document.RootElement;

        if (records.ValueKind != JsonValueKind.Array)
            return [];

        return records.EnumerateArray()
            .Select(record =>
            {
                var payload = record.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
                    ? payloadElement
                    : record;
                var kindValue = record.TryGetProperty("kind", out var kind) ? GetString(kind) ?? "" : "";
                return new DesiredRecord(
                    kindValue,
                    record.TryGetProperty("name", out var name) ? GetString(name) ?? "" : "",
                    record.TryGetProperty("payload", out var payloadValue) ? payloadValue.GetRawText() : "{}",
                    string.Equals(kindValue, "ArtifactReference", StringComparison.OrdinalIgnoreCase) ? ParseArtifactDescriptor(payload) : null);
            })
            .Where(record => !string.IsNullOrWhiteSpace(record.Kind) && !string.IsNullOrWhiteSpace(record.Name))
            .ToList();
    }

    private static IReadOnlyList<DeploymentDiffItem> Diff(IReadOnlyList<DesiredRecord> source, IReadOnlyList<DesiredRecord> target)
    {
        var sourceByKey = source.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var targetByKey = target.ToDictionary(x => x.Key, StringComparer.Ordinal);
        return sourceByKey.Keys.Concat(targetByKey.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key =>
            {
                sourceByKey.TryGetValue(key, out var sourceRecord);
                targetByKey.TryGetValue(key, out var targetRecord);
                var impact = targetRecord is null ? DiffImpact.Added :
                    sourceRecord is null ? DiffImpact.Removed :
                    string.Equals(sourceRecord.Payload, targetRecord.Payload, StringComparison.Ordinal) ? (DiffImpact?)null : DiffImpact.Changed;
                return impact is null
                    ? null
                    : new DeploymentDiffItem(
                        key,
                        Category(sourceRecord?.Kind ?? targetRecord!.Kind),
                        sourceRecord?.Name ?? targetRecord!.Name,
                        sourceRecord?.Payload ?? "",
                        targetRecord?.Payload ?? "",
                        impact.Value);
            })
            .Where(item => item is not null)
            .Cast<DeploymentDiffItem>()
            .ToList();
    }

    private static IReadOnlyList<PromotionArtifactComparison> CompareArtifacts(
        IReadOnlyList<DesiredRecord> source,
        IReadOnlyList<DesiredRecord> target,
        WorkspaceWorkflowEngine? engine,
        WorkflowEngineRegistration? engineRegistration)
    {
        var sourceByName = source
            .Where(x => x.Artifact is not null)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var targetByName = target
            .Where(x => x.Artifact is not null)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        return sourceByName.Keys.Concat(targetByName.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                sourceByName.TryGetValue(name, out var sourceRecord);
                targetByName.TryGetValue(name, out var targetRecord);
                var sourceArtifact = sourceRecord?.Artifact;
                var targetArtifact = targetRecord?.Artifact;
                var impact = targetArtifact is null ? PromotionArtifactImpact.Added :
                    sourceArtifact is null ? PromotionArtifactImpact.Removed :
                    SameArtifact(sourceArtifact, targetArtifact) ? PromotionArtifactImpact.Unchanged : PromotionArtifactImpact.Changed;
                return new PromotionArtifactComparison(
                    name,
                    sourceArtifact,
                    targetArtifact,
                    impact,
                    ValidateArtifactRuntimeCompatibility(name, sourceArtifact, engine, engineRegistration));
            })
            .ToList();
    }

    private static bool SameArtifact(PromotionArtifactDescriptor source, PromotionArtifactDescriptor target) =>
        string.Equals(source.ArtifactRecordId, target.ArtifactRecordId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(source.ArtifactId, target.ArtifactId, StringComparison.Ordinal)
        && string.Equals(source.ArtifactTypeId, target.ArtifactTypeId, StringComparison.Ordinal)
        && string.Equals(source.ContentDigest?.Algorithm, target.ContentDigest?.Algorithm, StringComparison.OrdinalIgnoreCase)
        && string.Equals(source.ContentDigest?.Value, target.ContentDigest?.Value, StringComparison.Ordinal)
        && SameProperties(source.Metadata, target.Metadata)
        && SameProperties(source.Configuration, target.Configuration)
        && source.CompatibilityHints.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(target.CompatibilityHints.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static bool SameProperties(IReadOnlyDictionary<string, string> source, IReadOnlyDictionary<string, string> target) =>
        source.Count == target.Count
        && source
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .SequenceEqual(target.OrderBy(x => x.Key, StringComparer.Ordinal));

    private static IReadOnlyList<DeploymentValidation> ValidateArtifactRuntimeCompatibility(
        string artifactName,
        PromotionArtifactDescriptor? artifact,
        WorkspaceWorkflowEngine? engine,
        WorkflowEngineRegistration? engineRegistration)
    {
        if (artifact is null)
            return [];

        var validations = new List<DeploymentValidation>();
        if (string.IsNullOrWhiteSpace(artifact.ArtifactId))
            validations.Add(new DeploymentValidation($"artifact-{artifactName}-id", ValidationSeverity.Blocker, "Artifact", $"{artifactName} artifact identity is missing."));
        if (string.IsNullOrWhiteSpace(artifact.ArtifactTypeId))
            validations.Add(new DeploymentValidation($"artifact-{artifactName}-type", ValidationSeverity.Blocker, "Artifact", $"{artifactName} artifact type is missing."));
        if (artifact.ContentDigest is null)
            validations.Add(new DeploymentValidation($"artifact-{artifactName}-digest", ValidationSeverity.Blocker, "Artifact", $"{artifactName} artifact digest is missing."));

        if (engine is null)
            validations.Add(new DeploymentValidation($"artifact-{artifactName}-engine", ValidationSeverity.Blocker, "Runtime compatibility", "Target engine is not visible in this workspace."));
        else if (engineRegistration is not null)
        {
            var artifactTypeId = artifact.ArtifactTypeId ?? "";
            var engineCapabilities = engineRegistration.Capabilities
                .Select(x => ArtifactApplyCapability.Normalize(artifactTypeId, x.Id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = artifact.CompatibilityHints
                .Select(hint => ArtifactApplyCapability.Normalize(artifactTypeId, hint))
                .Where(hint => !engineCapabilities.Contains(hint))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            validations.AddRange(missing.Select(capability =>
                new DeploymentValidation(
                    $"artifact-{artifactName}-capability-{capability}",
                    ValidationSeverity.Blocker,
                    "Runtime compatibility",
                    $"{artifactName} requires runtime capability {capability}.")));
        }

        return validations.Count == 0
            ? [new DeploymentValidation($"artifact-{artifactName}-compatible", ValidationSeverity.Pass, "Runtime compatibility", $"{artifactName} is compatible with the target runtime.")]
            : validations;
    }

    private static PromotionArtifactDescriptor? ParseArtifactDescriptor(JsonElement payload)
    {
        var artifactRecordId = payload.TryGetProperty("artifactRecordId", out var artifactRecordIdElement)
            ? GetString(artifactRecordIdElement)
            : null;
        var artifactId = payload.TryGetProperty("artifactId", out var artifactIdElement)
            ? GetString(artifactIdElement)
            : null;
        var artifactTypeId = payload.TryGetProperty("artifactTypeId", out var artifactTypeIdElement)
            ? GetString(artifactTypeIdElement)
            : null;
        var digest = payload.TryGetProperty("contentDigest", out var digestElement) && digestElement.ValueKind == JsonValueKind.Object
            ? ParseDigest(digestElement)
            : null;

        if (string.IsNullOrWhiteSpace(artifactRecordId)
            && string.IsNullOrWhiteSpace(artifactId)
            && string.IsNullOrWhiteSpace(artifactTypeId)
            && digest is null)
            return null;

        return new PromotionArtifactDescriptor(
            artifactRecordId,
            artifactId,
            artifactTypeId,
            digest,
            ReadSafeProperties(payload, "safeMetadata", "metadata", "displayMetadata"),
            ReadSafeProperties(payload, "configuration", "configurationOverlay", "configurationOverlays"),
            ReadCompatibilityHints(payload));
    }

    private static PromotionArtifactDigest? ParseDigest(JsonElement digestElement)
    {
        var algorithm = digestElement.TryGetProperty("algorithm", out var algorithmElement) ? GetString(algorithmElement) : null;
        var value = digestElement.TryGetProperty("value", out var valueElement) ? GetString(valueElement) : null;
        return string.IsNullOrWhiteSpace(algorithm) || string.IsNullOrWhiteSpace(value)
            ? null
            : new PromotionArtifactDigest(algorithm, value);
    }

    private static string? GetString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static IReadOnlyDictionary<string, string> ReadSafeProperties(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
                continue;

            return value.EnumerateObject()
                .Where(property => IsSafeScalar(property.Value))
                .ToDictionary(property => property.Name, property => SafeScalarValue(property.Value), StringComparer.Ordinal);
        }

        return new Dictionary<string, string>();
    }

    private static IReadOnlyList<string> ReadCompatibilityHints(JsonElement payload)
    {
        if (!payload.TryGetProperty("compatibilityHints", out var hints) || hints.ValueKind != JsonValueKind.Array)
            return [];

        return hints.EnumerateArray()
            .SelectMany(ReadRequiredCapabilities)
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadRequiredCapabilities(JsonElement hint)
    {
        if (!hint.TryGetProperty("requiredCapabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array)
            return [];

        return capabilities.EnumerateArray()
            .Where(capability => capability.ValueKind == JsonValueKind.String)
            .Select(capability => capability.GetString() ?? "")
            .ToList();
    }

    private static bool IsSafeScalar(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static string SafeScalarValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();

    private async Task<(EnvironmentSummary? Source, EnvironmentSummary? Target, WorkflowEngineRegistration? TargetEngine)> GetPromotionContextAsync(
        Guid workspaceId,
        WorkspacePromotionPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var cockpit = await store!.GetCockpitAsync(workspaceId, cancellationToken);
            var environments = cockpit.Applications.SelectMany(x => x.Environments).ToList();
            return (
                environments.SingleOrDefault(x => string.Equals(x.Id, request.SourceEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase)),
                environments.SingleOrDefault(x => string.Equals(x.Id, request.TargetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase)),
                cockpit.Engines.SingleOrDefault(x =>
                    string.Equals(x.Id, request.TargetEngineId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.EnvironmentId, request.TargetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase)));
        }
        catch (NotSupportedException)
        {
            return (null, null, null);
        }
    }

    private static IReadOnlyList<DeploymentValidation> Validate(
        IReadOnlyList<DesiredRecord> source,
        WorkspaceWorkflowEngine? engine,
        EnvironmentSummary? sourceEnvironment,
        EnvironmentSummary? targetEnvironment,
        Guid targetEnvironmentId)
    {
        var validations = new List<DeploymentValidation>();
        if (engine is null)
            validations.Add(new DeploymentValidation("deployment.engine.missing", ValidationSeverity.Blocker, "Engine", "Target engine is not visible in this workspace."));
        else if (engine.EnvironmentId != targetEnvironmentId)
            validations.Add(new DeploymentValidation("deployment.engine.environment-mismatch", ValidationSeverity.Blocker, "Engine", "Target engine does not belong to the target environment."));
        if (sourceEnvironment is not null && !DeploymentTierService.IsPromotionSource(sourceEnvironment))
            validations.Add(new DeploymentValidation("deployment.tier.source.unsupported", ValidationSeverity.Blocker, "Tier", $"{TierLabel(sourceEnvironment)} cannot be used as a promotion source."));
        if (targetEnvironment is not null && !DeploymentTierService.IsPromotionTarget(targetEnvironment))
            validations.Add(new DeploymentValidation("deployment.tier.target.unsupported", ValidationSeverity.Blocker, "Tier", $"{TierLabel(targetEnvironment)} cannot be used as a promotion target."));
        if (targetEnvironment is not null && DeploymentTierService.IsProductionLike(targetEnvironment))
            validations.Add(new DeploymentValidation("deployment.tier.production-like", ValidationSeverity.Warning, "Tier safeguards", $"{TierLabel(targetEnvironment)} applies production-grade safeguards."));
        if (targetEnvironment is not null && DeploymentTierService.RequiresConfirmation(targetEnvironment))
            validations.Add(new DeploymentValidation("deployment.tier.confirmation-required", ValidationSeverity.Warning, "Tier safeguards", $"{TierLabel(targetEnvironment)} requires explicit deployment confirmation."));
        if (targetEnvironment is not null && DeploymentTierService.CanRollback(targetEnvironment))
            validations.Add(new DeploymentValidation("deployment.tier.rollback-enabled", ValidationSeverity.Pass, "Tier safeguards", $"{TierLabel(targetEnvironment)} allows rollback actions."));

        if (targetEnvironment is null || DeploymentTierService.RequiresSecretVerification(targetEnvironment))
        {
            foreach (var secret in source.Where(x => string.Equals(x.Kind, "SecretReference", StringComparison.OrdinalIgnoreCase)))
            {
                using var payload = JsonDocument.Parse(secret.Payload);
                var reference = payload.RootElement.TryGetProperty("reference", out var value) ? GetString(value) : null;
                validations.Add(string.IsNullOrWhiteSpace(reference)
                    ? new DeploymentValidation($"secret-{secret.Name}", ValidationSeverity.Blocker, "Secret references", $"{secret.Name} secret reference is missing.")
                    : new DeploymentValidation($"secret-{secret.Name}", ValidationSeverity.Pass, "Secret references", $"{secret.Name} secret reference is present."));
            }
        }
        if (targetEnvironment is not null && DeploymentTierService.RequiresObservability(targetEnvironment) && !source.Any(x => string.Equals(x.Kind, "ObservabilityBinding", StringComparison.OrdinalIgnoreCase)))
            validations.Add(new DeploymentValidation("deployment.tier.observability-required", ValidationSeverity.Blocker, "Observability", $"{TierLabel(targetEnvironment)} requires at least one observability binding."));

        return validations.Count == 0
            ? [new DeploymentValidation("deployment.preview.valid", ValidationSeverity.Pass, "Deployment preview", "Promotion preview has no blockers.")]
            : validations;
    }

    private static string TierLabel(EnvironmentSummary environment) =>
        string.IsNullOrWhiteSpace(environment.TierName) ? environment.Tier.ToString() : environment.TierName;

    private static DiffCategory Category(string kind) =>
        kind switch
        {
            "Workflow" => DiffCategory.Workflows,
            "ArtifactReference" => DiffCategory.Workflows,
            "Feature" => DiffCategory.Features,
            "ShellConfiguration" => DiffCategory.ShellConfiguration,
            "RuntimeConfiguration" => DiffCategory.RuntimeConfiguration,
            "SecretReference" => DiffCategory.SecretReferences,
            "ObservabilityBinding" => DiffCategory.Observability,
            "EngineBinding" => DiffCategory.EngineBindings,
            _ => DiffCategory.RuntimeConfiguration
        };

    private sealed record DesiredRecord(string Kind, string Name, string Payload, PromotionArtifactDescriptor? Artifact)
    {
        public string Key => $"{Kind}:{Name}";
    }
}
