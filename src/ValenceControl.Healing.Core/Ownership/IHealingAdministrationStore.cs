namespace ValenceControl.Healing.Core.Ownership;

/// <summary>
/// Tenant-scoped persistence boundary for the owner-managed repair authority catalog.
/// Policy definitions are immutable; changes create a new version and existing bindings keep their snapshot reference.
/// </summary>
public interface IHealingAdministrationStore
{
    ValueTask<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PathPolicy>> ListPathPoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EvidencePolicy>> ListEvidencePoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MergePolicy>> ListMergePoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderConnection> SaveProviderConnectionAsync(
        ProviderConnection connection,
        CancellationToken cancellationToken = default);

    ValueTask SavePoliciesAsync(
        PathPolicy pathPolicy,
        EvidencePolicy evidencePolicy,
        MergePolicy mergePolicy,
        CancellationToken cancellationToken = default);
}
