namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class WorkspacePermissionService(IWorkspacePermissionStore store)
{
    public async Task<EffectiveWorkspacePermissions> GetEffectivePermissionsAsync(
        Guid workspaceId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var grants = await store.GetPermissionGrantsAsync(workspaceId, accountId, cancellationToken);
        var permissions = grants
            .Where(x => x.RevokedAt is null && WorkspaceDeploymentPermissions.All.Contains(x.Permission))
            .Select(x => x.Permission)
            .ToHashSet(StringComparer.Ordinal);

        return new EffectiveWorkspacePermissions(workspaceId, accountId, permissions);
    }

    public async Task RequireAsync(
        Guid workspaceId,
        Guid accountId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var effective = await GetEffectivePermissionsAsync(workspaceId, accountId, cancellationToken);
        if (!effective.Has(permission))
            throw new UnauthorizedAccessException($"Missing deployment permission '{permission}'.");
    }

    public Task<WorkspacePermissionGrant> GrantAsync(
        Guid workspaceId,
        GrantWorkspacePermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspaceDeploymentPermissions.All.Contains(request.Permission))
            throw new ArgumentException($"Unknown deployment permission '{request.Permission}'.", nameof(request));

        return store.GrantPermissionAsync(workspaceId, request, cancellationToken);
    }
}
