using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed class WorkspaceHealingIncidentEndpointModule : IHealingEndpointModule
{
    private const int DefaultTake = 50;
    private const int MaximumTake = 100;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing/incidents");
        group.MapGet("/", ListAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapGet("/{incidentId:guid}", GetAsync)
            .RequireHealingPermission(HealingPermissions.Read);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? environmentId,
        HealingIncidentStatus? status,
        IncidentSeverity? severity,
        bool? repairable,
        string? cursor,
        int? take,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (applicationId is not null &&
            !await ApplicationExistsAsync(cockpitService, workspaceId, applicationId.Value, cancellationToken))
        {
            return Problem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        }
        if (!TryDecodeCursor(cursor, out var offset))
            return Problem(context, StatusCodes.Status400BadRequest, "healing.incidents.cursor", "The incident cursor is invalid.");

        var pageSize = Math.Clamp(take ?? DefaultTake, 1, MaximumTake);
        var query = dbContext.HealingIncidents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationId == applicationId.Value);
        if (status is not null)
            query = query.Where(x => x.Status == status.Value);
        if (severity is not null)
            query = query.Where(x => x.Severity == severity.Value);
        if (repairable is not null)
            query = repairable.Value
                ? query.Where(x => x.SelectedBindingId != null &&
                                   x.Status != HealingIncidentStatus.ObservationOnly &&
                                   x.Status != HealingIncidentStatus.Suppressed)
                : query.Where(x => x.SelectedBindingId == null ||
                                   x.Status == HealingIncidentStatus.ObservationOnly ||
                                   x.Status == HealingIncidentStatus.Suppressed);
        if (environmentId is not null)
            query = query.Where(x => x.ActiveEpisodeId != null && dbContext.EnvironmentImpacts.Any(impact =>
                impact.WorkspaceId == workspaceId &&
                impact.ApplicationId == x.ApplicationId &&
                impact.EpisodeId == x.ActiveEpisodeId &&
                impact.EnvironmentId == environmentId.Value));
        var incidents = await query
            .OrderByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = incidents.Length > pageSize;
        var page = incidents.Take(pageSize).ToArray();
        var activeEpisodeIds = page.Where(x => x.ActiveEpisodeId is not null).Select(x => x.ActiveEpisodeId!.Value).ToArray();
        var impacts = activeEpisodeIds.Length == 0
            ? []
            : await dbContext.EnvironmentImpacts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && activeEpisodeIds.Contains(x.EpisodeId))
                .OrderBy(x => x.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var impactsByEpisode = impacts.ToLookup(x => x.EpisodeId);
        var items = page.Select(incident => ToSummary(
            incident,
            incident.ActiveEpisodeId is null ? [] : impactsByEpisode[incident.ActiveEpisodeId.Value].ToArray())).ToArray();
        var nextCursor = hasMore && page.Length > 0
            ? EncodeCursor(offset + page.Length)
            : null;
        return Results.Ok(new HealingIncidentListResponse(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext context,
        HealingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == incidentId,
            cancellationToken);
        if (incident is null)
            return Problem(context, StatusCodes.Status404NotFound, "healing.incident.not-found", "Incident was not found.");

        var episodes = await dbContext.IncidentEpisodes.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id)
            .OrderByDescending(x => x.OpenedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var episodeIds = episodes.Select(x => x.Id).ToArray();
        var impacts = episodeIds.Length == 0
            ? []
            : await dbContext.EnvironmentImpacts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ApplicationId == incident.ApplicationId &&
                            episodeIds.Contains(x.EpisodeId))
                .OrderBy(x => x.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var occurrences = await dbContext.IncidentOccurrences.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id)
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var occurrenceIds = occurrences.Select(x => x.Id).ToArray();
        var attributions = occurrenceIds.Length == 0
            ? []
            : await dbContext.ComponentAttributions.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ApplicationId == incident.ApplicationId &&
                            occurrenceIds.Contains(x.OccurrenceId))
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
        var workItem = await dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id &&
                        (incident.ActiveEpisodeId == null || x.EpisodeId == incident.ActiveEpisodeId))
            .OrderByDescending(x => x.LastObservedAt ?? x.LastProjectedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return Results.Ok(new HealingIncidentDetailResponse(
            incident.Id,
            incident.ApplicationId,
            incident.Status,
            incident.Severity,
            incident.Classification,
            incident.FirstSeenAt,
            incident.LastSeenAt,
            incident.OccurrenceCount,
            incident.ActiveEpisodeId,
            incident.SelectedBindingId is not null,
            incident.NeedsHumanReason,
            incident.ReadyAfter,
            episodes.Select(ToEpisode).ToArray(),
            impacts.Select(ToEnvironmentImpact).ToArray(),
            occurrences.Select(ToOccurrence).ToArray(),
            attributions.Select(ToAttribution).ToArray(),
            workItem is null ? null : ToWorkItem(workItem)));
    }

    private static HealingIncidentSummaryResponse ToSummary(
        HealingIncident incident,
        IReadOnlyList<EnvironmentImpact> impacts) => new(
        incident.Id,
        incident.ApplicationId,
        incident.Status,
        incident.Severity,
        incident.Classification,
        incident.FirstSeenAt,
        incident.LastSeenAt,
        incident.OccurrenceCount,
        incident.ActiveEpisodeId,
        incident.SelectedBindingId is not null,
        incident.NeedsHumanReason,
        incident.ReadyAfter,
        impacts.Select(ToEnvironmentImpact).ToArray());

    private static HealingIncidentEpisodeResponse ToEpisode(IncidentEpisode episode) => new(
        episode.Id,
        episode.PreviousEpisodeId,
        episode.OpenedAt,
        episode.ClosedAt,
        ParseStringArray(episode.ProducingRevisionsJson),
        episode.TargetRevision,
        episode.Outcome,
        episode.RegressionReason);

    private static HealingEnvironmentImpactResponse ToEnvironmentImpact(EnvironmentImpact impact) => new(
        impact.EpisodeId,
        impact.EnvironmentId,
        impact.FirstSeenAt,
        impact.LastSeenAt,
        impact.OccurrenceCount,
        ParseStringArray(impact.ProducingRevisionsJson),
        impact.CurrentDeployedRevision,
        impact.VerificationStatus,
        impact.OccurrenceThreshold,
        impact.DebounceWindow,
        impact.ThresholdReachedAt,
        impact.ReadyAfter);

    private static HealingIncidentOccurrenceResponse ToOccurrence(IncidentOccurrence occurrence) => new(
        occurrence.Id,
        occurrence.EnvironmentId,
        occurrence.RevisionId,
        occurrence.OccurredAt,
        occurrence.AcceptedAt,
        occurrence.Classification,
        occurrence.Severity,
        occurrence.ExceptionType,
        occurrence.OperationName,
        occurrence.RetryState,
        occurrence.EvidenceTier);

    private static HealingComponentAttributionResponse ToAttribution(ComponentAttribution attribution) => new(
        attribution.Id,
        attribution.OccurrenceId,
        attribution.ComponentEntryId,
        attribution.BindingId,
        attribution.Confidence,
        attribution.Basis,
        attribution.Resolution,
        ParseStringArray(attribution.ReasonCodesJson));

    private static HealingWorkItemSummaryResponse ToWorkItem(RepairWorkItemProjection workItem) => new(
        workItem.Id,
        workItem.EpisodeId,
        workItem.Number,
        workItem.Url,
        workItem.ProviderState,
        workItem.ProjectionStatus,
        workItem.LastProjectedAt,
        workItem.LastObservedAt);

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<bool> ApplicationExistsAsync(
        DeploymentCockpitService service,
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken) =>
        (await service.GetCockpitAsync(workspaceId, cancellationToken)).Applications.Any(x =>
            Guid.TryParse(x.Id, out var id) && id == applicationId);

    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor))
            return true;
        try
        {
            return int.TryParse(
                       Encoding.UTF8.GetString(Convert.FromBase64String(cursor)),
                       System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(context, status, code, detail);
}

public sealed record HealingIncidentListResponse(
    IReadOnlyList<HealingIncidentSummaryResponse> Items,
    string? NextCursor);

public sealed record HealingIncidentSummaryResponse(
    Guid Id,
    Guid ApplicationId,
    HealingIncidentStatus Status,
    IncidentSeverity Severity,
    IncidentClassification Classification,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    Guid? ActiveEpisodeId,
    bool Repairable,
    NeedsHumanReason? NeedsHumanReason,
    DateTimeOffset? ReadyAfter,
    IReadOnlyList<HealingEnvironmentImpactResponse> EnvironmentImpacts);

public sealed record HealingIncidentDetailResponse(
    Guid Id,
    Guid ApplicationId,
    HealingIncidentStatus Status,
    IncidentSeverity Severity,
    IncidentClassification Classification,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    Guid? ActiveEpisodeId,
    bool Repairable,
    NeedsHumanReason? NeedsHumanReason,
    DateTimeOffset? ReadyAfter,
    IReadOnlyList<HealingIncidentEpisodeResponse> Episodes,
    IReadOnlyList<HealingEnvironmentImpactResponse> EnvironmentImpacts,
    IReadOnlyList<HealingIncidentOccurrenceResponse> Occurrences,
    IReadOnlyList<HealingComponentAttributionResponse> Attributions,
    HealingWorkItemSummaryResponse? WorkItem);

public sealed record HealingIncidentEpisodeResponse(
    Guid Id,
    Guid? PreviousEpisodeId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<string> ProducingRevisions,
    string? TargetRevision,
    IncidentEpisodeOutcome Outcome,
    string? RegressionReason);

public sealed record HealingEnvironmentImpactResponse(
    Guid EpisodeId,
    Guid EnvironmentId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    IReadOnlyList<string> ProducingRevisions,
    string? CurrentDeployedRevision,
    VerificationOutcome VerificationStatus,
    int OccurrenceThreshold,
    TimeSpan DebounceWindow,
    DateTimeOffset? ThresholdReachedAt,
    DateTimeOffset? ReadyAfter);

public sealed record HealingIncidentOccurrenceResponse(
    Guid Id,
    Guid EnvironmentId,
    Guid? RevisionId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AcceptedAt,
    IncidentClassification Classification,
    IncidentSeverity Severity,
    string ExceptionType,
    string OperationName,
    IncidentRetryState RetryState,
    EvidenceTier EvidenceTier);

public sealed record HealingComponentAttributionResponse(
    Guid Id,
    Guid OccurrenceId,
    Guid ComponentEntryId,
    Guid? BindingId,
    decimal Confidence,
    AttributionBasis Basis,
    AttributionResolution Resolution,
    IReadOnlyList<string> ReasonCodes);

public sealed record HealingWorkItemSummaryResponse(
    Guid Id,
    Guid EpisodeId,
    long? Number,
    string? Url,
    string? ProviderState,
    WorkItemProjectionStatus ProjectionStatus,
    DateTimeOffset? LastProjectedAt,
    DateTimeOffset? LastObservedAt);
