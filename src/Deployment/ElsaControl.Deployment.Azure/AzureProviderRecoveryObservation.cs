using System.Security.Cryptography;
using System.Text;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Safe, typed metadata captured by read-only provider reconciliation before an
/// explicit recovery request. It describes one provider postcondition only; it
/// is not a second lifecycle state machine or a serialized provider response.
/// </summary>
public sealed record AzureProviderRecoveryObservationRecord(
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid InstanceId,
    Guid LifecycleOperationId,
    ElsaInstanceOperationAction LifecycleAction,
    int ObservedLifecycleAttemptNumber,
    int ObservedInstanceVersion,
    Guid ProviderOperationId,
    string ProviderOperationIdentity,
    string ProviderRequestHash,
    int ProviderAttemptNumber,
    long ProviderVersion,
    long ProviderCheckpointSequence,
    Guid ProviderAssignmentId,
    string TargetKey,
    string? ProviderScopeFingerprint,
    string ResolvedPlanId,
    int ResolvedPlanSchemaVersion,
    string ResolvedPlanUri,
    string ResolvedPlanContentHash,
    string ProviderPlanFingerprint,
    string ProviderTemplateFingerprint,
    AzureProviderRunnerStep CompletedStep,
    AzureProviderOperationPhase ObservedPhase,
    AzureProviderHealth ObservedHealth,
    string ResourceFingerprint,
    string PostconditionFingerprint,
    DateTimeOffset ObservedAt)
{
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || WorkspaceId == Guid.Empty || InstanceId == Guid.Empty ||
            LifecycleOperationId == Guid.Empty || ProviderOperationId == Guid.Empty ||
            ProviderAssignmentId == Guid.Empty)
            throw new ArgumentException("Recovery observation ownership is invalid.");
        if (!Enum.IsDefined(LifecycleAction) || LifecycleAction == ElsaInstanceOperationAction.Delete ||
            !Enum.IsDefined(CompletedStep) || !AzureProviderRecoveryObservationSupport.IsSupportedCompletedStep(CompletedStep) ||
            !Enum.IsDefined(ObservedPhase) || !Enum.IsDefined(ObservedHealth))
            throw new ArgumentException("Recovery observation enum values are invalid.");
        if (ObservedLifecycleAttemptNumber < 1 || ObservedInstanceVersion < 1 ||
            ProviderAttemptNumber < 1 || ProviderVersion < 1 || ProviderCheckpointSequence < 0 ||
            ResolvedPlanSchemaVersion < 1)
            throw new ArgumentException("Recovery observation versions are invalid.");
        RequireSafeToken(ProviderOperationIdentity, nameof(ProviderOperationIdentity), 64);
        RequireFingerprint(ProviderRequestHash, nameof(ProviderRequestHash));
        RequireSafeToken(TargetKey, nameof(TargetKey), 128);
        ElsaResolvedPlanReference planReference;
        try
        {
            planReference = new ElsaResolvedPlanReference(
                ResolvedPlanId,
                ResolvedPlanSchemaVersion,
                ResolvedPlanContentHash,
                ResolvedPlanUri);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("Resolved plan reference is invalid.", nameof(ResolvedPlanUri), exception);
        }
        if (!string.Equals(planReference.PlanId, ResolvedPlanId, StringComparison.Ordinal) ||
            !string.Equals(planReference.ContentHash, ResolvedPlanContentHash, StringComparison.Ordinal) ||
            !string.Equals(planReference.PlanUri, ResolvedPlanUri, StringComparison.Ordinal))
            throw new ArgumentException("Resolved plan reference is not canonical.", nameof(ResolvedPlanUri));
        RequireFingerprint(ProviderPlanFingerprint, nameof(ProviderPlanFingerprint));
        RequireFingerprint(ProviderTemplateFingerprint, nameof(ProviderTemplateFingerprint));
        RequireFingerprint(ResourceFingerprint, nameof(ResourceFingerprint));
        RequireFingerprint(PostconditionFingerprint, nameof(PostconditionFingerprint));
        if (ProviderScopeFingerprint is not null)
            RequireFingerprint(ProviderScopeFingerprint, nameof(ProviderScopeFingerprint));
        if (ObservedAt == default)
            throw new ArgumentException("Recovery observation time is required.", nameof(ObservedAt));
    }

    /// <summary>
    /// The natural idempotency key deliberately excludes the generated record ID
    /// and polling time. Unchanged polls therefore address one immutable row.
    /// </summary>
    public string ComputeNaturalKey()
    {
        Validate();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(includeRecordId: null))));
    }

    public static string ComputeNaturalKey(AzureProviderRecoveryObservationRecord observation) =>
        observation?.ComputeNaturalKey() ?? throw new ArgumentNullException(nameof(observation));

    public string ComputeRecordDigest(Guid recordId)
    {
        if (recordId == Guid.Empty)
            throw new ArgumentException("Recovery observation record ID is required.", nameof(recordId));
        Validate();
        return "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(recordId))));
    }

    public static string ComputeResourceFingerprint(AzureProviderResourceReferences resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var canonical = string.Join('\n',
            resources.ResourceGroupName,
            resources.FoundationDeploymentId,
            resources.WorkloadDeploymentId,
            resources.WorkloadResourceId,
            resources.WorkloadRevisionName,
            resources.StableTrafficRevisionName,
            resources.WorkloadIdentityResourceId,
            resources.WorkloadIdentityClientId,
            resources.WorkloadIdentityPrincipalId,
            resources.KeyVaultResourceId,
            resources.KeyVaultUri,
            resources.SqlServerResourceId,
            resources.SqlServerFqdn,
            resources.ContainerAppsEnvironmentResourceId,
            resources.RegistryResourceId,
            resources.AcrPullDeploymentId,
            resources.AcrPullRoleAssignmentId);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string ComputePostconditionFingerprint(
        AzureProviderRecoveryObservation observation,
        string resourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        RequireFingerprint(resourceFingerprint, nameof(resourceFingerprint));
        var canonical = string.Join('\n', observation.Kind, observation.CompletedStep,
            observation.Health, resourceFingerprint, observation.Code);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private string Canonical(Guid? includeRecordId) => string.Join('\n',
        includeRecordId?.ToString("N"),
        OrganizationId.ToString("N"),
        WorkspaceId.ToString("N"),
        InstanceId.ToString("N"),
        LifecycleOperationId.ToString("N"),
        LifecycleAction,
        ObservedLifecycleAttemptNumber,
        ObservedInstanceVersion,
        ProviderOperationId.ToString("N"),
        ProviderOperationIdentity,
        ProviderRequestHash,
        ProviderAttemptNumber,
        ProviderVersion,
        ProviderCheckpointSequence,
        ProviderAssignmentId.ToString("N"),
        TargetKey,
        ProviderScopeFingerprint,
        ResolvedPlanId,
        ResolvedPlanSchemaVersion,
        ResolvedPlanUri,
        ResolvedPlanContentHash,
        ProviderPlanFingerprint,
        ProviderTemplateFingerprint,
        CompletedStep,
        ObservedPhase,
        ObservedHealth,
        ResourceFingerprint,
        PostconditionFingerprint);

    private static string RequireSafeToken(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength ||
            value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ':')))
            throw new ArgumentException($"{name} is invalid.", name);
        return value;
    }

    private static void RequireFingerprint(string? value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new ArgumentException($"{name} must be a SHA-256 fingerprint.", name);
    }
}

/// <summary>
/// Recovery steps that have an explicit phase mapping. The concrete Azure observer currently
/// proves foundation and the preceding ACR Pull checkpoint; later steps remain valid extension
/// points for observers that can prove their own postconditions.
/// </summary>
internal static class AzureProviderRecoveryObservationSupport
{
    public static bool IsSupportedCompletedStep(AzureProviderRunnerStep step) => step switch
    {
        AzureProviderRunnerStep.Foundation or
        AzureProviderRunnerStep.AcrPull or
        AzureProviderRunnerStep.SeedSecrets or
        AzureProviderRunnerStep.SqlBootstrap or
        AzureProviderRunnerStep.Workload or
        AzureProviderRunnerStep.Health or
        AzureProviderRunnerStep.Promotion => true,
        _ => false
    };

    /// <summary>
    /// Foundation-only observation is safe only while the durable operation still contains no
    /// handles produced by a later provider step. ACR and secret-seeding uncertainty share the
    /// historical FoundationSubmitted phase, so those handles are the durable discriminator.
    /// </summary>
    public static bool IsFoundationOnlyEligible(AzureProviderOperation operation) =>
        (operation.Phase is AzureProviderOperationPhase.Planned or AzureProviderOperationPhase.FoundationSubmitted) &&
        (operation.AttemptedStep is null or AzureProviderRunnerStep.Foundation) &&
        operation.Resources.RegistryResourceId is null &&
        operation.Resources.AcrPullDeploymentId is null &&
        operation.Resources.AcrPullRoleAssignmentId is null &&
        operation.Resources.WorkloadDeploymentId is null &&
        operation.Resources.WorkloadResourceId is null &&
        operation.Resources.WorkloadRevisionName is null &&
        operation.Resources.StableTrafficRevisionName is null;

    /// <summary>
    /// ACR Pull is an independently observable checkpoint after foundation. It is eligible only
    /// while the operation has not retained any workload or traffic handle; proving it never
    /// authorizes secret seeding, SQL bootstrap, workload, health, or traffic completion.
    /// </summary>
    public static bool IsAcrPullEligible(AzureProviderOperation operation) =>
        (operation.Phase is AzureProviderOperationPhase.Planned or AzureProviderOperationPhase.FoundationSubmitted) &&
        operation.AttemptedStep == AzureProviderRunnerStep.AcrPull &&
        operation.Resources.ResourceGroupName is not null &&
        operation.Resources.FoundationDeploymentId is not null &&
        operation.Resources.WorkloadIdentityResourceId is not null &&
        operation.Resources.WorkloadIdentityClientId is not null &&
        operation.Resources.WorkloadIdentityPrincipalId is not null &&
        operation.Resources.KeyVaultResourceId is not null &&
        operation.Resources.KeyVaultUri is not null &&
        operation.Resources.SqlServerResourceId is not null &&
        operation.Resources.SqlServerFqdn is not null &&
        operation.Resources.ContainerAppsEnvironmentResourceId is not null &&
        operation.Resources.RegistryResourceId is not null &&
        operation.Resources.AcrPullDeploymentId is not null &&
        operation.Resources.WorkloadDeploymentId is null &&
        operation.Resources.WorkloadResourceId is null &&
        operation.Resources.WorkloadRevisionName is null &&
        operation.Resources.StableTrafficRevisionName is null;
}

public sealed record AzureProviderRecoveryObservationReceipt(
    Guid RecordId,
    string Reference,
    string Digest,
    AzureProviderRecoveryObservationRecord Observation);

/// <summary>
/// The immutable evidence captured before recovery is accepted, together with the
/// append-only recovery envelope that consumed it. The observed attempt/version
/// deliberately describe the pre-Recover snapshot; accepted values describe the
/// incremented lifecycle operation and aggregate after the acceptance transaction.
/// </summary>
public sealed record AzureProviderRecoveryObservationBinding(
    Guid RecoveryRequestId,
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid InstanceId,
    Guid LifecycleOperationId,
    int ObservedLifecycleAttemptNumber,
    int ObservedInstanceVersion,
    int AcceptedLifecycleAttemptNumber,
    int AcceptedInstanceVersion,
    string IdempotencyScope,
    string IdempotencyKey,
    string RequestHash,
    string Reference,
    string Digest)
{
    public void Validate()
    {
        if (RecoveryRequestId == Guid.Empty || OrganizationId == Guid.Empty ||
            WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || LifecycleOperationId == Guid.Empty ||
            ObservedLifecycleAttemptNumber < 1 || ObservedInstanceVersion < 1 ||
            AcceptedLifecycleAttemptNumber < 2 || AcceptedInstanceVersion < 1 ||
            string.IsNullOrWhiteSpace(IdempotencyScope) || IdempotencyScope.Length > 256 ||
            string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128 ||
            IdempotencyKey.Any(char.IsControl) || RequestHash is null || RequestHash.Length != 64 ||
            RequestHash.AsSpan().ContainsAnyExcept("0123456789abcdef") ||
            !ElsaInstanceProviderRecoveryObservationReference.TryParse(Reference, out _, out var referenceDigest) ||
            !string.Equals(referenceDigest, Digest, StringComparison.Ordinal))
            throw new ArgumentException("Recovery observation binding is invalid.");
    }
}

public interface IAzureProviderRecoveryObservationStore
{
    Task<AzureProviderRecoveryObservationReceipt> CreateOrGetAsync(
        AzureProviderRecoveryObservationRecord observation,
        CancellationToken cancellationToken = default);

    Task<AzureProviderRecoveryObservationRecord?> GetAndValidateRecordedAsync(
        Guid organizationId,
        Guid workspaceId,
        Guid instanceId,
        Guid lifecycleOperationId,
        int observedLifecycleAttemptNumber,
        string reference,
        string digest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a proof after explicit recovery acceptance. This method binds the
    /// proof to the append-only recovery ledger before checking the post-acceptance
    /// lifecycle version, so the pre-Recover provider/lifecycle tuple is not
    /// incorrectly compared with legitimate incremented state.
    /// </summary>
    Task<AzureProviderRecoveryObservationRecord?> GetAndValidateForAcceptedRecoveryAsync(
        AzureProviderRecoveryObservationBinding binding,
        CancellationToken cancellationToken = default);
}
