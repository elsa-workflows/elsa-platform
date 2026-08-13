using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Healing;

public sealed class HealingHumanCommandCoordinator(
    HealingDbContext dbContext,
    IGitHubRepositoryPermissionProvider providerPermissions,
    WorkspacePermissionService workspacePermissions,
    HumanProviderCommandService commands)
{
    private static readonly IReadOnlySet<string> NoWorkspacePermissions =
        new HashSet<string>(StringComparer.Ordinal);

    public async ValueTask<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var commandId = await dbContext.HumanCommands.AsNoTracking()
            .Where(x => x.Status == HumanCommandStatus.Pending)
            .OrderBy(x => x.RequestedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (commandId is null)
            return false;

        return await ExecuteAsync(commandId.Value, null, false, cancellationToken);
    }

    public async ValueTask<bool> ExecuteAsync(
        Guid commandId,
        Guid? confirmationId,
        bool confirmationValid,
        CancellationToken cancellationToken = default)
    {
        var command = await dbContext.HumanCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == commandId &&
                (x.Status == HumanCommandStatus.Pending || x.Status == HumanCommandStatus.Authorized),
                cancellationToken);
        if (command is null)
            return false;

        var authorities = await (
            from projection in dbContext.RepairWorkItemProjections.AsNoTracking()
            join providerConnection in dbContext.ProviderConnections.AsNoTracking()
                on new { projection.WorkspaceId, Id = projection.ProviderConnectionId }
                equals new { providerConnection.WorkspaceId, providerConnection.Id }
            where projection.WorkspaceId == command.WorkspaceId &&
                  projection.ApplicationId == command.ApplicationId &&
                  projection.IncidentId == command.IncidentId &&
                  projection.ProjectionStatus == WorkItemProjectionStatus.Current &&
                  providerConnection.Status == ProviderConnectionStatus.Active
            select providerConnection)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (authorities.Length != 1)
        {
            await commands.ExecuteAsync(command.Id, new(
                false,
                "unavailable",
                null,
                NoWorkspacePermissions), cancellationToken);
            return true;
        }

        var provider = authorities[0];

        var permission = await providerPermissions.GetAsync(
            new(provider.Id, provider.RepositoryProviderId,
                provider.RepositoryOwner, provider.RepositoryName),
            command.ProviderActorId,
            command.ProviderActorLogin,
            cancellationToken);
        var link = await dbContext.ProviderActorIdentityLinks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == command.WorkspaceId &&
            x.ProviderConnectionId == provider.Id &&
            x.ProviderActorId == command.ProviderActorId &&
            x.RevokedAt == null,
            cancellationToken);
        var effective = link is null
            ? null
            : await workspacePermissions.GetEffectivePermissionsAsync(
                command.WorkspaceId, link.ControlAccountId, cancellationToken);
        await commands.ExecuteAsync(command.Id, new(
            permission.IsMaintainer,
            permission.Permission,
            link?.ControlAccountId,
            effective?.Permissions ?? new HashSet<string>(StringComparer.Ordinal),
            confirmationId,
            confirmationValid), cancellationToken);
        return true;
    }
}
