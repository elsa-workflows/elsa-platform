namespace ElsaControl.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentTierStore
{
    Task<IReadOnlyList<WorkspaceDeploymentTier>> ListTiersAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentTier?> GetTierAsync(
        Guid workspaceId,
        Guid tierId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentTier> CreateTierAsync(
        Guid workspaceId,
        CreateDeploymentTierRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentTier> UpdateTierAsync(
        Guid workspaceId,
        Guid tierId,
        UpdateDeploymentTierRequest request,
        DeploymentTierImpactSummary impact,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentTier> ArchiveTierAsync(
        Guid workspaceId,
        Guid tierId,
        ArchiveDeploymentTierRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentTier> RestoreTierAsync(
        Guid workspaceId,
        Guid tierId,
        RestoreDeploymentTierRequest request,
        CancellationToken cancellationToken = default);

    Task<DeploymentTierImpactSummary> PreviewTierImpactAsync(
        Guid workspaceId,
        Guid tierId,
        IReadOnlyList<string> proposedCapabilities,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceDeploymentTier>> EnsureDefaultTiersAsync(
        Guid workspaceId,
        Guid? actorAccountId = null,
        CancellationToken cancellationToken = default);
}
