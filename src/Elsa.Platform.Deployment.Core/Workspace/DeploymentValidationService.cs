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
        var validations = Validate(sourceRecords, engine);

        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            source.RevisionNumber,
            target?.RevisionNumber ?? 0,
            diff,
            validations,
            target?.RevisionNumber);
    }

    public PromotionComparison PreviewPromotion(WorkspacePromotionPreviewRequest request)
    {
        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            0,
            0,
            [],
            [new DeploymentValidation("deployment.preview.not-implemented", ValidationSeverity.Blocker, "Deployment preview", "Promotion preview is not implemented yet.")],
            null);
    }

    private static PromotionComparison Blocked(WorkspacePromotionPreviewRequest request, string id, string message) =>
        new(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            0,
            0,
            [],
            [new DeploymentValidation(id, ValidationSeverity.Blocker, "Deployment preview", message)],
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

    private static IReadOnlyList<DeploymentValidation> Validate(IReadOnlyList<DesiredRecord> source, WorkspaceWorkflowEngine? engine)
    {
        var validations = new List<DeploymentValidation>();
        if (engine is null)
            validations.Add(new DeploymentValidation("deployment.engine.missing", ValidationSeverity.Blocker, "Engine", "Target engine is not visible in this workspace."));

        foreach (var secret in source.Where(x => string.Equals(x.Kind, "SecretReference", StringComparison.OrdinalIgnoreCase)))
        {
            using var payload = JsonDocument.Parse(secret.Payload);
            var reference = payload.RootElement.TryGetProperty("reference", out var value) ? value.GetString() : null;
            validations.Add(string.IsNullOrWhiteSpace(reference)
                ? new DeploymentValidation($"secret-{secret.Name}", ValidationSeverity.Blocker, "Secret references", $"{secret.Name} secret reference is missing.")
                : new DeploymentValidation($"secret-{secret.Name}", ValidationSeverity.Pass, "Secret references", $"{secret.Name} secret reference is present."));
        }

        return validations.Count == 0
            ? [new DeploymentValidation("deployment.preview.valid", ValidationSeverity.Pass, "Deployment preview", "Promotion preview has no blockers.")]
            : validations;
    }

    private static DiffCategory Category(string kind) =>
        kind switch
        {
            "Workflow" => DiffCategory.Workflows,
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
