using System.Collections.ObjectModel;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Safe submission contract for the Azure provider. The plan is already below the resolved-plan
/// admission boundary; it contains only immutable identities, placement facts and secret
/// locators. It must never be populated from an unadmitted customer payload.
/// </summary>
public sealed record AzureProviderOperationSubmission(
    string IdempotencyKey,
    string TemplateFingerprint,
    AzureWorkloadPlan Plan);

public sealed record AzureProviderOperationStatusResponse(
    AzureProviderOperation Operation,
    IReadOnlyList<AzureProviderOperationTransition> Transitions);

public interface IAzureProviderOperationService
{
    Task<AzureProviderOperation> SubmitAsync(
        Guid workspaceId,
        AzureProviderOperationSubmission submission,
        CancellationToken cancellationToken = default);

    Task<AzureProviderOperation> SubmitDeleteAsync(
        Guid workspaceId,
        AzureProviderOperationSubmission submission,
        CancellationToken cancellationToken = default);

    Task<AzureProviderOperationStatusResponse?> GetStatusAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates durable Azure operations from an admitted provider plan. The operation store retains
/// the safe provider-plan fields needed for a worker to recover after a process restart; raw
/// resolved-plan JSON, credentials and secret values are never accepted.
/// </summary>
public sealed class AzureProviderOperationService(
    IAzureProviderOperationStore store,
    TimeProvider? timeProvider = null) : IAzureProviderOperationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<AzureProviderOperation> SubmitAsync(
        Guid workspaceId,
        AzureProviderOperationSubmission submission,
        CancellationToken cancellationToken = default) =>
        SubmitCoreAsync(workspaceId, submission, AzureProviderOperationAction.Reconcile, cancellationToken);

    public Task<AzureProviderOperation> SubmitDeleteAsync(
        Guid workspaceId,
        AzureProviderOperationSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        // Idempotency is scoped to a lifecycle action. A cleanup request must never
        // accidentally reuse the successful reconcile operation for the same target.
        return SubmitCoreAsync(
            workspaceId,
            submission with { IdempotencyKey = $"{submission.IdempotencyKey}:delete" },
            AzureProviderOperationAction.Delete,
            cancellationToken);
    }

    public async Task<AzureProviderOperationStatusResponse?> GetStatusAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID is required.", nameof(operationId));

        var operation = await store.GetAsync(workspaceId, operationId, cancellationToken);
        if (operation is null)
            return null;

        return new AzureProviderOperationStatusResponse(
            operation,
            await store.ListTransitionsAsync(workspaceId, operationId, cancellationToken));
    }

    private async Task<AzureProviderOperation> SubmitCoreAsync(
        Guid workspaceId,
        AzureProviderOperationSubmission submission,
        AzureProviderOperationAction action,
        CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submission.Plan);

        var plan = submission.Plan;
        ValidateSubmissionPlan(plan, nameof(submission));
        if (!IsFingerprint(submission.TemplateFingerprint))
            throw new ArgumentException("A compiled template fingerprint is required.", nameof(submission));

        var operationRequest = CreateOperationRequest(
            workspaceId,
            submission.IdempotencyKey,
            submission.TemplateFingerprint,
            plan,
            action);
        return await store.CreateOrGetAsync(operationRequest, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public static AzureProviderOperationRequest CreateOperationRequest(
        Guid workspaceId,
        string idempotencyKey,
        string templateFingerprint,
        AzureWorkloadPlan plan,
        AzureProviderOperationAction action = AzureProviderOperationAction.Reconcile) =>
        new(
            workspaceId,
            plan.WorkloadName,
            action,
            idempotencyKey,
            plan.Fingerprint,
            templateFingerprint,
            plan.ElsaVersion,
            plan.ReleaseLine,
            plan.Topology,
            plan.Isolation,
            plan.Location,
            plan.ImageRepository,
            $"sha256:{plan.ImageDigest}",
            plan.ReleaseManifestDigest,
            plan.ReleaseManifestSignatureDigest,
            plan.ReleaseManifestReference,
            plan.ReleaseManifestSignatureReference,
            new ReadOnlyDictionary<string, string>((plan.SecretReferences ?? new Dictionary<string, string>()).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)));

    internal static AzureWorkloadPlan? TryRestorePlan(AzureProviderOperation operation)
    {
        if (operation is null || string.IsNullOrWhiteSpace(operation.ReleaseManifestReference) ||
            string.IsNullOrWhiteSpace(operation.ReleaseManifestSignatureReference) ||
            string.IsNullOrWhiteSpace(operation.ImageDigest) ||
            operation.ImageDigest.Length != "sha256:".Length + 64 ||
            !operation.ImageDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !operation.ImageDigest["sha256:".Length..].All(Uri.IsHexDigit) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(operation.ReleaseManifestReference, operation.ReleaseManifestDigest) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(operation.ReleaseManifestSignatureReference, operation.ReleaseManifestSignatureDigest) ||
            !AzureProviderOperationValidation.IsSafeSecretReferences(operation.SafeSecretReferences))
            return null;

        return new AzureWorkloadPlan(
            operation.TargetKey,
            operation.Location,
            operation.ElsaVersion,
            operation.ReleaseLine,
            operation.Topology,
            operation.Isolation,
            operation.ImageRepository,
            operation.ImageDigest["sha256:".Length..],
            operation.ReleaseManifestReference,
            operation.ReleaseManifestDigest!,
            operation.ReleaseManifestSignatureReference,
            operation.ReleaseManifestSignatureDigest!,
            operation.SafeSecretReferences,
            operation.PlanFingerprint);
    }

    private static void ValidateSubmissionPlan(AzureWorkloadPlan plan, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(plan.WorkloadName) ||
            !string.Equals(plan.WorkloadName, plan.WorkloadName.Trim(), StringComparison.Ordinal) ||
            plan.WorkloadName.Length is < 3 or > 16 ||
            !char.IsAsciiLetterOrDigit(plan.WorkloadName[0]) ||
            !char.IsAsciiLetterOrDigit(plan.WorkloadName[^1]) ||
            plan.WorkloadName.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-'))
            throw new ArgumentException("The provider workload name is invalid.", parameterName);
        if (string.IsNullOrWhiteSpace(plan.Location) ||
            !string.Equals(plan.Location, plan.Location.Trim(), StringComparison.Ordinal) ||
            plan.Location.Any(char.IsControl))
            throw new ArgumentException("The provider location is invalid.", parameterName);
        if (!IsFingerprint(plan.Fingerprint))
            throw new ArgumentException("The provider plan fingerprint is invalid.", parameterName);
        if (string.IsNullOrWhiteSpace(plan.ImageRepository) ||
            !plan.ImageRepository.StartsWith($"{AzureWorkloadPlanTranslator.SupportedRegistryHost}/", StringComparison.Ordinal) ||
            plan.ImageRepository.Any(char.IsWhiteSpace) || plan.ImageRepository.Any(char.IsControl))
            throw new ArgumentException("The provider image repository is invalid.", parameterName);
        if (plan.ImageDigest is null || plan.ImageDigest.Length != 64 || !plan.ImageDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("The provider image digest is invalid.", parameterName);
        if (!AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(
                plan.ReleaseManifestReference, plan.ReleaseManifestDigest) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(
                plan.ReleaseManifestSignatureReference, plan.ReleaseManifestSignatureDigest))
            throw new ArgumentException("Provider evidence references must be safe immutable locators.", parameterName);
        if (!AzureProviderOperationValidation.IsSafeSecretReferences(plan.SecretReferences))
            throw new ArgumentException("Provider secret references must be safe immutable locators.", parameterName);
    }

    private static bool IsFingerprint(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
}
