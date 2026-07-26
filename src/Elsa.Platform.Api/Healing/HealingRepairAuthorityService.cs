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
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        if (!await HasConfiguredAuthorityAsync(
                workspaceId, applicationId, episodeId, providerConnectionId, cancellationToken))
            return false;

        return await (
            from incident in dbContext.HealingIncidents.AsNoTracking()
            join episode in dbContext.IncidentEpisodes.AsNoTracking()
                on new { incident.WorkspaceId, incident.ApplicationId, IncidentId = incident.Id }
                equals new { episode.WorkspaceId, episode.ApplicationId, episode.IncidentId }
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { incident.WorkspaceId, incident.ApplicationId, Id = incident.SelectedBindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, Id = (Guid?)binding.Id }
            where incident.WorkspaceId == workspaceId && incident.ApplicationId == applicationId &&
                  incident.Id == incidentId && incident.ActiveEpisodeId == episodeId &&
                  episode.Id == episodeId && episode.Outcome == IncidentEpisodeOutcome.Active &&
                  binding.ProviderConnectionId == providerConnectionId && binding.Status == SourceOwnershipBindingStatus.Active
            select incident.Id).AnyAsync(cancellationToken);
    }

    public async ValueTask<bool> CanMutateAttemptAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        Guid providerConnectionId,
        Guid attemptId,
        RepairAttemptStatus compatibleStatus,
        CancellationToken cancellationToken = default)
    {
        if (!await HasConfiguredAuthorityAsync(
                workspaceId, applicationId, episodeId, providerConnectionId, cancellationToken))
            return false;

        return await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join incident in dbContext.HealingIncidents.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.IncidentId }
                equals new { incident.WorkspaceId, incident.ApplicationId, incident.Id }
            join episode in dbContext.IncidentEpisodes.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EpisodeId }
                equals new { episode.WorkspaceId, episode.ApplicationId, episode.Id }
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            where attempt.WorkspaceId == workspaceId && attempt.ApplicationId == applicationId &&
                  attempt.Id == attemptId && attempt.EpisodeId == episodeId && attempt.Status == compatibleStatus &&
                  incident.ActiveEpisodeId == episodeId && episode.Outcome == IncidentEpisodeOutcome.Active &&
                  binding.ProviderConnectionId == providerConnectionId && binding.Status == SourceOwnershipBindingStatus.Active
            select attempt.Id).AnyAsync(cancellationToken);
    }

    private async ValueTask<bool> HasConfiguredAuthorityAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        Guid providerConnectionId,
        CancellationToken cancellationToken)
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
        var environmentPolicies = environments.ToDictionary(x => x.EnvironmentId);
        return environmentIds.Length > 0 && environmentIds.All(environmentId =>
            environmentPolicies.TryGetValue(environmentId, out var environment) &&
            !environment.EnvironmentKillSwitch &&
            (environment.RepairEnabled ?? application.RepairEnabled));
    }
}
