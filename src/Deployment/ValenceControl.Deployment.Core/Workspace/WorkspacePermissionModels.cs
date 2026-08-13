namespace ValenceControl.Deployment.Core.Workspace;

public static class WorkspaceDeploymentPermissions
{
    public const string Read = "deployments.read";
    public const string ManageSetup = "deployments.setup.manage";
    public const string ManageDesiredState = "deployments.desired-state.manage";
    public const string PreviewPromotion = "deployments.promotion.preview";
    public const string ExecuteDeployment = "deployments.run.execute";
    public const string ExecuteRollback = "deployments.rollback.execute";
    public const string ExecuteControls = "deployments.controls.execute";
    public const string ManageObservability = "deployments.observability.manage";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Read,
        ManageSetup,
        ManageDesiredState,
        PreviewPromotion,
        ExecuteDeployment,
        ExecuteRollback,
        ExecuteControls,
        ManageObservability
    };
}

public sealed record WorkspacePermissionGrant(
    Guid Id,
    Guid WorkspaceId,
    Guid AccountId,
    string Permission,
    Guid? GrantedByAccountId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RevokedAt,
    Guid? RevokedByAccountId = null);

public sealed record GrantWorkspacePermissionRequest(
    Guid AccountId,
    string Permission,
    Guid? GrantedByAccountId);

public sealed record RevokeWorkspacePermissionRequest(
    Guid AccountId,
    string Permission,
    Guid? RevokedByAccountId);

public sealed record RevokeWorkspacePermissionResult(
    IReadOnlyList<WorkspacePermissionGrant> Grants,
    bool Changed);

public enum WorkspacePermissionAuditAction
{
    Granted,
    Revoked
}

public sealed record WorkspacePermissionAuditRecord(
    Guid Id,
    Guid WorkspaceId,
    Guid GrantId,
    Guid AccountId,
    string Permission,
    WorkspacePermissionAuditAction Action,
    Guid? ActorAccountId,
    DateTimeOffset OccurredAt);

public sealed record WorkspacePermissionCatalog(
    IReadOnlySet<string> All,
    IReadOnlySet<string> OwnerDefaults);

public sealed record EffectiveWorkspacePermissions(
    Guid WorkspaceId,
    Guid AccountId,
    IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => Permissions.Contains(permission);
}
