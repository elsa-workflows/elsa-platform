using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

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
        var diff = Diff(sourceRecords, targetRecords);
        var (sourceEnvironment, targetEnvironment) = await GetPromotionEnvironmentsAsync(workspaceId, request, cancellationToken);
        var validations = Validate(sourceRecords, engine, sourceEnvironment, targetEnvironment);

        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            source.Id.ToString("D"),
            source.RevisionNumber,
            target?.RevisionNumber ?? 0,
            diff,
            validations,
            target?.RevisionNumber,
            target?.Id.ToString("D"));
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
            .Select(record => new DesiredRecord(
                record.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                record.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                record.TryGetProperty("payload", out var payload) ? payload.GetRawText() : "{}"))
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

    private async Task<(EnvironmentSummary? Source, EnvironmentSummary? Target)> GetPromotionEnvironmentsAsync(
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
                environments.SingleOrDefault(x => string.Equals(x.Id, request.TargetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase)));
        }
        catch (NotSupportedException)
        {
            return (null, null);
        }
    }

    private static IReadOnlyList<DeploymentValidation> Validate(
        IReadOnlyList<DesiredRecord> source,
        WorkspaceWorkflowEngine? engine,
        EnvironmentSummary? sourceEnvironment,
        EnvironmentSummary? targetEnvironment)
    {
        var validations = new List<DeploymentValidation>();
        if (engine is null)
            validations.Add(new DeploymentValidation("deployment.engine.missing", ValidationSeverity.Blocker, "Engine", "Target engine is not visible in this workspace."));
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
                var reference = payload.RootElement.TryGetProperty("reference", out var value) ? value.GetString() : null;
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

    private sealed record DesiredRecord(string Kind, string Name, string Payload)
    {
        public string Key => $"{Kind}:{Name}";
    }
}
