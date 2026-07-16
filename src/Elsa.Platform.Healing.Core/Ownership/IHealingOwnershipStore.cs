namespace Elsa.Platform.Healing.Core.Ownership;

public sealed record OwnershipWriteResult<T>(T Value, bool IsReplay, bool IsConsistentReplay = true);
public sealed record ManifestRegistrationWriteResult(
    ComponentManifest Value,
    bool IsReplay,
    string? FailureReasonCode = null);

/// <summary>
/// Tenant-scoped persistence boundary used by Healing configuration and ownership decisions.
/// Implementations must preserve optimistic concurrency and atomic trust-state transitions.
/// </summary>
public interface IHealingOwnershipStore
{
    ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    ValueTask<HealingConfiguration?> GetConfigurationAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingWorkspaceConfiguration?> GetWorkspaceConfigurationAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingWorkspaceConfiguration> UpsertWorkspaceConfigurationAsync(
        HealingWorkspaceConfiguration configuration,
        CancellationToken cancellationToken = default);

    ValueTask<HealingConfiguration> SaveConfigurationAsync(
        HealingConfiguration configuration,
        CancellationToken cancellationToken = default);

    ValueTask<OwnershipWriteResult<ComponentManifest>> AddManifestAsync(
        ComponentManifest manifest,
        CancellationToken cancellationToken = default);

    ValueTask<ManifestRegistrationWriteResult> RegisterManifestAsync(
        ComponentManifest manifest,
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken = default);

    ValueTask<ComponentManifest?> GetManifestAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ComponentManifest>> ListManifestsAsync(
        Guid workspaceId,
        Guid applicationId,
        bool trustedOnly,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TransitionManifestTrustAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        ComponentManifestTrustState expected,
        ComponentManifestTrustState target,
        string actorId,
        string method,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SourceOwnershipBinding>> ListBindingsAsync(
        Guid workspaceId,
        Guid applicationId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    ValueTask<SourceOwnershipBinding?> GetBindingAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderConnection?> GetProviderConnectionAsync(
        Guid workspaceId,
        Guid providerConnectionId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> PoliciesAreTrustedAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid pathPolicyId,
        Guid evidencePolicyId,
        Guid mergePolicyId,
        CancellationToken cancellationToken = default);

    ValueTask<SourceOwnershipBinding> SaveBindingAsync(
        SourceOwnershipBinding binding,
        CancellationToken cancellationToken = default);
}
