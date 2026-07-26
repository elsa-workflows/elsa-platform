using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingReportingStore(HealingDbContext dbContext) : IHealingReportingStore
{
    private static readonly RepairAttemptStatus[] ActiveAttemptStatuses =
    [
        RepairAttemptStatus.Queued,
        RepairAttemptStatus.Dispatched,
        RepairAttemptStatus.Running,
        RepairAttemptStatus.ProposalReady,
        RepairAttemptStatus.ResultReceived,
        RepairAttemptStatus.Publishing
    ];
    private static readonly RepairAttemptStatus[] BlockedAttemptStatuses =
    [RepairAttemptStatus.Failed, RepairAttemptStatus.Stopped, RepairAttemptStatus.Expired];

    public async ValueTask<HealingOverviewSource> LoadOverviewAsync(
        HealingOverviewQuery query,
        CancellationToken cancellationToken = default)
    {
        var configurationsQuery = dbContext.HealingConfigurations.AsNoTracking()
            .Where(x => x.WorkspaceId == query.WorkspaceId);
        var environmentsQuery = dbContext.HealingEnvironmentConfigurations.AsNoTracking()
            .Where(x => x.WorkspaceId == query.WorkspaceId);
        var incidentsQuery = dbContext.HealingIncidents.AsNoTracking()
            .Where(x => x.WorkspaceId == query.WorkspaceId);
        var attemptsQuery = dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.WorkspaceId == query.WorkspaceId);
        var providerOperationsQuery = dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == query.WorkspaceId);

        if (query.ApplicationId is { } applicationId)
        {
            configurationsQuery = configurationsQuery.Where(x => x.ApplicationId == applicationId);
            environmentsQuery = environmentsQuery.Where(x => x.ApplicationId == applicationId);
            incidentsQuery = incidentsQuery.Where(x => x.ApplicationId == applicationId);
            attemptsQuery = attemptsQuery.Where(x => x.ApplicationId == applicationId);
            providerOperationsQuery = providerOperationsQuery.Where(x => x.ApplicationId == applicationId);
        }
        if (query.EnvironmentId is { } environmentId)
        {
            environmentsQuery = environmentsQuery.Where(x => x.EnvironmentId == environmentId);
            incidentsQuery = incidentsQuery.Where(x => x.ActiveEpisodeId != null && dbContext.EnvironmentImpacts.Any(impact =>
                impact.WorkspaceId == query.WorkspaceId &&
                impact.EpisodeId == x.ActiveEpisodeId &&
                impact.EnvironmentId == environmentId));
        }
        if (query.Status is { } status)
            incidentsQuery = incidentsQuery.Where(x => x.Status == status);
        if (query.Severity is { } severity)
            incidentsQuery = incidentsQuery.Where(x => x.Severity == severity);
        if (query.Repairable is { } repairable)
        {
            incidentsQuery = repairable
                ? incidentsQuery.Where(x => x.SelectedBindingId != null &&
                                            x.Status != HealingIncidentStatus.Healed &&
                                            x.Status != HealingIncidentStatus.Superseded &&
                                            x.Status != HealingIncidentStatus.Waived &&
                                            x.Status != HealingIncidentStatus.Failed &&
                                            x.Status != HealingIncidentStatus.ObservationOnly &&
                                            x.Status != HealingIncidentStatus.Suppressed)
                : incidentsQuery.Where(x => x.SelectedBindingId == null ||
                                            x.Status == HealingIncidentStatus.Healed ||
                                            x.Status == HealingIncidentStatus.Superseded ||
                                            x.Status == HealingIncidentStatus.Waived ||
                                            x.Status == HealingIncidentStatus.Failed ||
                                            x.Status == HealingIncidentStatus.ObservationOnly ||
                                            x.Status == HealingIncidentStatus.Suppressed);
        }
        if (query.From is { } from)
        {
            incidentsQuery = incidentsQuery.Where(x => x.LastSeenAt >= from);
            attemptsQuery = attemptsQuery.Where(x => (x.CompletedAt ?? x.StartedAt) >= from);
            providerOperationsQuery = providerOperationsQuery.Where(x => x.CreatedAt >= from);
        }
        if (query.To is { } to)
        {
            incidentsQuery = incidentsQuery.Where(x => x.LastSeenAt <= to);
            attemptsQuery = attemptsQuery.Where(x => (x.CompletedAt ?? x.StartedAt) <= to);
            providerOperationsQuery = providerOperationsQuery.Where(x => x.CreatedAt <= to);
        }

        if (query.EnvironmentId is not null || query.Status is not null || query.Severity is not null || query.Repairable is not null)
        {
            attemptsQuery = attemptsQuery.Where(attempt => incidentsQuery.Any(incident => incident.Id == attempt.IncidentId));
            providerOperationsQuery = providerOperationsQuery.Where(operation =>
                operation.IncidentId == null || incidentsQuery.Any(incident => incident.Id == operation.IncidentId));
        }

        var configurations = await configurationsQuery.OrderBy(x => x.ApplicationId).ToArrayAsync(cancellationToken);
        var environments = await environmentsQuery.OrderBy(x => x.EnvironmentId).ToArrayAsync(cancellationToken);
        var totalIncidents = await incidentsQuery.LongCountAsync(cancellationToken);
        var repairableIncidents = await incidentsQuery.LongCountAsync(x =>
            x.SelectedBindingId != null &&
            x.Status != HealingIncidentStatus.Healed &&
            x.Status != HealingIncidentStatus.Superseded &&
            x.Status != HealingIncidentStatus.Waived &&
            x.Status != HealingIncidentStatus.Failed &&
            x.Status != HealingIncidentStatus.ObservationOnly &&
            x.Status != HealingIncidentStatus.Suppressed,
            cancellationToken);
        var openIncidents = await incidentsQuery.LongCountAsync(x =>
            x.Status != HealingIncidentStatus.Healed &&
            x.Status != HealingIncidentStatus.Superseded &&
            x.Status != HealingIncidentStatus.Waived, cancellationToken);
        var incidentStateRows = await incidentsQuery.GroupBy(x => x.Status)
            .Select(group => new { Name = group.Key, Count = group.LongCount() })
            .ToArrayAsync(cancellationToken);
        var severityRows = await incidentsQuery.GroupBy(x => x.Severity)
            .Select(group => new { Name = group.Key, Count = group.LongCount() })
            .ToArrayAsync(cancellationToken);
        var impactsQuery = dbContext.EnvironmentImpacts.AsNoTracking().Where(impact =>
            impact.WorkspaceId == query.WorkspaceId &&
            (query.EnvironmentId == null || impact.EnvironmentId == query.EnvironmentId) &&
            incidentsQuery.Any(incident => incident.ActiveEpisodeId == impact.EpisodeId));
        var verificationRows = await impactsQuery.GroupBy(x => x.VerificationStatus)
            .Select(group => new { Name = group.Key, Count = group.LongCount() })
            .ToArrayAsync(cancellationToken);
        var activeAttempts = await attemptsQuery.LongCountAsync(
            x => ActiveAttemptStatuses.Contains(x.Status), cancellationToken);
        var blockedAttempts = await attemptsQuery.LongCountAsync(x =>
            BlockedAttemptStatuses.Contains(x.Status) ||
            incidentsQuery.Any(incident => incident.Id == x.IncidentId && incident.Status == HealingIncidentStatus.NeedsHuman),
            cancellationToken);
        var pullRequestsQuery =
            from pullRequest in dbContext.RepairPullRequests.AsNoTracking()
            join attempt in attemptsQuery on pullRequest.AttemptId equals attempt.Id
            where pullRequest.WorkspaceId == query.WorkspaceId
            select new { PullRequest = pullRequest, Attempt = attempt };
        var openPullRequests = await pullRequestsQuery.LongCountAsync(
            x => x.PullRequest.MergeState == PullRequestMergeState.Open, cancellationToken);
        var blockedPullRequests = await pullRequestsQuery.LongCountAsync(x =>
            x.PullRequest.MergeState == PullRequestMergeState.Open &&
            (x.PullRequest.ClosureReason != null || BlockedAttemptStatuses.Contains(x.Attempt.Status) ||
             incidentsQuery.Any(incident =>
                 incident.Id == x.Attempt.IncidentId && incident.Status == HealingIncidentStatus.NeedsHuman)),
            cancellationToken);
        var recentIncidents = await incidentsQuery
            .Where(x => x.Status != HealingIncidentStatus.Healed &&
                        x.Status != HealingIncidentStatus.Superseded &&
                        x.Status != HealingIncidentStatus.Waived)
            .OrderByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.Id)
            .Take(20)
            .Select(x => new HealingOverviewIncident(
                x.Id,
                x.ApplicationId,
                x.Status,
                x.Severity,
                x.Classification,
                x.OccurrenceCount,
                x.SelectedBindingId != null &&
                x.Status != HealingIncidentStatus.Healed &&
                x.Status != HealingIncidentStatus.Superseded &&
                x.Status != HealingIncidentStatus.Waived &&
                x.Status != HealingIncidentStatus.Failed &&
                x.Status != HealingIncidentStatus.ObservationOnly &&
                x.Status != HealingIncidentStatus.Suppressed,
                x.LastSeenAt))
            .ToArrayAsync(cancellationToken);
        var usage = await BuildUsageAsync(
            configurations,
            attemptsQuery,
            providerOperationsQuery,
            query.From,
            query.To,
            cancellationToken);

        return new(
            configurations,
            environments,
            openIncidents,
            incidentStateRows.OrderBy(x => x.Name).Select(x => new HealingNamedCount(x.Name.ToString(), x.Count)).ToArray(),
            severityRows.OrderBy(x => x.Name).Select(x => new HealingNamedCount(x.Name.ToString(), x.Count)).ToArray(),
            new(repairableIncidents, totalIncidents - repairableIncidents),
            new(activeAttempts, blockedAttempts, openPullRequests, blockedPullRequests),
            verificationRows.OrderBy(x => x.Name).Select(x => new HealingNamedCount(x.Name.ToString(), x.Count)).ToArray(),
            usage,
            recentIncidents);
    }

    private static async ValueTask<HealingUsageReport> BuildUsageAsync(
        IReadOnlyList<HealingConfiguration> configurations,
        IQueryable<RepairAttempt> attempts,
        IQueryable<ProviderOperation> providerOperations,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var attemptCount = await attempts.LongCountAsync(cancellationToken);
        var completedAttempts = await attempts.LongCountAsync(
            x => x.Status == RepairAttemptStatus.Succeeded || x.Status == RepairAttemptStatus.PullRequestOpen,
            cancellationToken);
        var failedAttempts = await attempts.LongCountAsync(
            x => BlockedAttemptStatuses.Contains(x.Status), cancellationToken);
        var providerOperationCount = await providerOperations.LongCountAsync(cancellationToken);
        var failedProviderOperations = await providerOperations.LongCountAsync(
            x => x.Status == ProviderOperationStatus.Failed || x.Status == ProviderOperationStatus.DeadLettered,
            cancellationToken);

        var usage = await attempts.GroupBy(_ => 1).Select(group => new
        {
            InputUnits = group.Sum(x => x.InputUnits),
            OutputUnits = group.Sum(x => x.OutputUnits),
            AgentDurationTicks = group.Sum(x => x.AgentDurationTicks),
            RepositoryRunDurationTicks = group.Sum(x => x.RepositoryRunDurationTicks),
            RepositoryRuns = group.Sum(x => x.RepositoryRuns)
        }).SingleOrDefaultAsync(cancellationToken);

        return new(
            from,
            to,
            attemptCount,
            completedAttempts,
            failedAttempts,
            usage?.InputUnits ?? 0,
            usage?.OutputUnits ?? 0,
            TimeSpan.FromTicks(usage?.AgentDurationTicks ?? 0).TotalSeconds,
            TimeSpan.FromTicks(usage?.RepositoryRunDurationTicks ?? 0).TotalSeconds,
            usage?.RepositoryRuns ?? 0,
            providerOperationCount,
            failedProviderOperations,
            configurations.Sum(item => (long)item.InferenceBudget),
            configurations.Sum(item => (long)item.RepositoryRunBudget),
            configurations.Sum(x => x.TimeBudget.TotalSeconds),
            configurations.Sum(item => (long)item.ConcurrencyBudget));
    }

    public async ValueTask<HealingAuditSourcePage> LoadAuditAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? incidentId,
        HealingAuditCursor? before,
        int take,
        CancellationToken cancellationToken = default)
    {
        var aggregateIds = applicationId is not null || incidentId is not null
            ? ResolveAggregateIds(workspaceId, applicationId, incidentId)
            : null;

        var query = dbContext.HealingAuditEvents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (aggregateIds is not null)
        {
            query = applicationId is not null && incidentId is null
                ? query.Where(x => aggregateIds.Contains(x.AggregateId) ||
                                   aggregateIds.Contains(x.CorrelationId) ||
                                   x.CausationId == applicationId)
                : query.Where(x => aggregateIds.Contains(x.AggregateId) || aggregateIds.Contains(x.CorrelationId));
        }
        if (before is { } cursor)
            query = query.Where(x => x.Sequence < cursor.Sequence ||
                                     (x.Sequence == cursor.Sequence && x.Id.CompareTo(cursor.Id) < 0));

        var items = await query
            .OrderByDescending(x => x.Sequence)
            .ThenByDescending(x => x.Id)
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        return new(items.Take(take).ToArray(), items.Length > take);
    }

    private IQueryable<Guid> ResolveAggregateIds(
        Guid workspaceId,
        Guid? applicationId,
        Guid? incidentId)
    {
        var incidentsQuery = dbContext.HealingIncidents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (applicationId is { } appId)
            incidentsQuery = incidentsQuery.Where(x => x.ApplicationId == appId);
        if (incidentId is { } selectedIncidentId)
            incidentsQuery = incidentsQuery.Where(x => x.Id == selectedIncidentId);
        var incidentIds = incidentsQuery.Select(x => x.Id);
        var applicationIds = incidentsQuery.Select(x => x.ApplicationId);
        if (applicationId is { } requestedApplicationId)
        {
            applicationIds = applicationIds.Concat(dbContext.HealingConfigurations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == requestedApplicationId)
                .Select(x => x.ApplicationId));
        }

        var episodeIds = dbContext.IncidentEpisodes.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id);
        var occurrenceIds = dbContext.IncidentOccurrences.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id);
        var attemptIds = dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id);
        var aggregateIds = applicationIds.Concat(incidentIds).Concat(episodeIds).Concat(occurrenceIds).Concat(attemptIds);

        aggregateIds = aggregateIds.Concat(dbContext.EnvironmentImpacts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && episodeIds.Contains(x.EpisodeId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.ComponentAttributions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && occurrenceIds.Contains(x.OccurrenceId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.ManagedRepairProposals.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && attemptIds.Contains(x.AttemptId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.RepairResults.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && attemptIds.Contains(x.AttemptId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.RepairPullRequests.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && attemptIds.Contains(x.AttemptId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.PolicyEvaluations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.AttemptId != null && attemptIds.Contains(x.AttemptId.Value))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        ((x.IncidentId != null && incidentIds.Contains(x.IncidentId.Value)) ||
                         (x.AttemptId != null && attemptIds.Contains(x.AttemptId.Value))))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.HumanCommands.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.EvidenceBundles.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(dbContext.EvidenceAccessDecisions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && incidentIds.Contains(x.IncidentId))
            .Select(x => x.Id));
        var verificationRecords = dbContext.VerificationResults.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && episodeIds.Contains(x.EpisodeId));
        aggregateIds = aggregateIds.Concat(verificationRecords.Select(x => x.Id));
        aggregateIds = aggregateIds.Concat(verificationRecords
            .Where(x => x.DeploymentObservationId.HasValue)
            .Select(x => x.DeploymentObservationId!.Value));

        if (incidentId is null)
        {
            var applicationBindings = dbContext.SourceOwnershipBindings.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            (applicationId == null
                                ? applicationIds.Contains(x.ApplicationId)
                                : x.ApplicationId == applicationId));
            var providerConnectionIds = applicationBindings.Select(x => x.ProviderConnectionId);
            aggregateIds = aggregateIds.Concat(dbContext.HealingConfigurations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.HealingEnvironmentConfigurations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.ComponentManifests.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(applicationBindings.Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(providerConnectionIds);
            aggregateIds = aggregateIds.Concat(dbContext.ProviderActorIdentityLinks.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && providerConnectionIds.Contains(x.ProviderConnectionId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.PathPolicies.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.EvidencePolicies.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.MergePolicies.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
            aggregateIds = aggregateIds.Concat(dbContext.DeploymentObservations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && applicationIds.Contains(x.ApplicationId))
                .Select(x => x.Id));
        }

        return aggregateIds.Distinct();
    }
}
