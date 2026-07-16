using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

/// <summary>Revalidates current tenant, stage, environment, provider, and binding authority before each mutation.</summary>
public sealed class HealingRepairAuthorityService(
    HealingDbContext dbContext,
    IOptions<HealingOptions> options)
{
    private readonly HealingOptions _options = options.Value;

    public async ValueTask<bool> CanMutateAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        Guid providerConnectionId,
        Guid? incidentId,
        Guid? attemptId,
        CancellationToken cancellationToken = default)
    {
        if (_options.PlatformKillSwitch || !_options.RepairDispatchEnabled)
            return false;
        var workspace = await dbContext.HealingWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId, cancellationToken);
        var application = await dbContext.HealingConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId, cancellationToken);
        var providerActive = await dbContext.ProviderConnections.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspaceId && x.Id == providerConnectionId && x.Status == ProviderConnectionStatus.Active,
            cancellationToken);
        if (workspace is null || application is null || workspace.WorkspaceKillSwitch ||
            application.ApplicationKillSwitch || !application.RepairEnabled || !providerActive)
            return false;

        var environmentIds = await dbContext.EnvironmentImpacts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EpisodeId == episodeId)
            .Select(x => x.EnvironmentId)
            .ToArrayAsync(cancellationToken);
        var environments = await dbContext.HealingEnvironmentConfigurations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && environmentIds.Contains(x.EnvironmentId))
            .ToArrayAsync(cancellationToken);
        if (!environmentIds.Any(environmentId => environments.SingleOrDefault(x => x.EnvironmentId == environmentId) is
                { EnvironmentKillSwitch: false } environment && environment.RepairEnabled != false))
            return false;

        if (attemptId is not null)
        {
            return await (
                from attempt in dbContext.RepairAttempts.AsNoTracking()
                join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                    on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                    equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
                where attempt.WorkspaceId == workspaceId && attempt.ApplicationId == applicationId &&
                      attempt.Id == attemptId && attempt.EpisodeId == episodeId &&
                      binding.ProviderConnectionId == providerConnectionId && binding.Status == SourceOwnershipBindingStatus.Active
                select attempt.Id).AnyAsync(cancellationToken);
        }

        return incidentId is not null && await (
            from incident in dbContext.HealingIncidents.AsNoTracking()
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { incident.WorkspaceId, incident.ApplicationId, Id = incident.SelectedBindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, Id = (Guid?)binding.Id }
            where incident.WorkspaceId == workspaceId && incident.ApplicationId == applicationId &&
                  incident.Id == incidentId && incident.ActiveEpisodeId == episodeId &&
                  binding.ProviderConnectionId == providerConnectionId && binding.Status == SourceOwnershipBindingStatus.Active
            select incident.Id).AnyAsync(cancellationToken);
    }
}
