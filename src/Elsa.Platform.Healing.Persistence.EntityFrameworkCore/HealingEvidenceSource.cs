using System.Text.Json;
using Elsa.Platform.Healing.Core.Repairs;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

/// <summary>
/// Builds bounded evidence fields exclusively from Platform-owned, post-redaction incident projections.
/// </summary>
public sealed class HealingEvidenceSource(HealingDbContext dbContext) : IHealingEvidenceSource
{
    public async ValueTask<EvidenceSourceSnapshot> ReadAsync(
        EvidenceSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == request.WorkspaceId &&
                 x.ApplicationId == request.ApplicationId &&
                 x.Id == request.IncidentId,
            cancellationToken);
        if (incident is null)
            return Empty();

        var episode = incident.ActiveEpisodeId is null
            ? null
            : await dbContext.IncidentEpisodes.AsNoTracking().SingleOrDefaultAsync(
                x => x.WorkspaceId == request.WorkspaceId &&
                     x.ApplicationId == request.ApplicationId &&
                     x.Id == incident.ActiveEpisodeId,
                cancellationToken);
        var occurrences = await dbContext.IncidentOccurrences.AsNoTracking()
            .Where(x => x.WorkspaceId == request.WorkspaceId &&
                        x.ApplicationId == request.ApplicationId &&
                        x.IncidentId == request.IncidentId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Take(20)
            .ToArrayAsync(cancellationToken);
        var latest = occurrences.FirstOrDefault();
        var impacts = episode is null
            ? []
            : await dbContext.EnvironmentImpacts.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId &&
                            x.ApplicationId == request.ApplicationId &&
                            x.EpisodeId == episode.Id)
                .OrderBy(x => x.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var occurrenceIds = occurrences.Select(x => x.Id).ToArray();
        var attributions = occurrenceIds.Length == 0
            ? []
            : await dbContext.ComponentAttributions.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId &&
                            x.ApplicationId == request.ApplicationId &&
                            occurrenceIds.Contains(x.OccurrenceId))
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.Id)
                .Take(20)
                .ToArrayAsync(cancellationToken);

        var values = request.Fields.ToDictionary(field => field, field => field switch
        {
            EvidenceField.ExceptionType => latest?.ExceptionType,
            EvidenceField.OperationName => latest?.OperationName,
            EvidenceField.NormalizedStack => latest?.NormalizedStackJson,
            EvidenceField.OccurrenceWindow => Serialize(new
            {
                incident.FirstSeenAt,
                incident.LastSeenAt,
                incident.OccurrenceCount
            }),
            EvidenceField.AffectedEnvironments => Serialize(impacts.Select(x => new
            {
                x.EnvironmentId,
                x.FirstSeenAt,
                x.LastSeenAt,
                x.OccurrenceCount,
                x.CurrentDeployedRevision
            })),
            EvidenceField.ProducingRevisions => episode?.ProducingRevisionsJson,
            EvidenceField.ComponentAttribution => Serialize(attributions.Select(x => new
            {
                x.ComponentEntryId,
                x.BindingId,
                x.Confidence,
                basis = x.Basis.ToString(),
                resolution = x.Resolution.ToString(),
                reasonCodes = ParseJson(x.ReasonCodesJson)
            })),
            EvidenceField.TraceCorrelation => latest is null
                ? null
                : Serialize(new { latest.TraceId, latest.SpanId }),
            EvidenceField.SafeAttributes => null,
            _ => null
        });
        var provenance = request.Fields.ToDictionary(x => x, _ => "platform-incident-projection");
        return new EvidenceSourceSnapshot(values, provenance);
    }

    private static EvidenceSourceSnapshot Empty() =>
        new(new Dictionary<EvidenceField, string?>(), new Dictionary<EvidenceField, string>());

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static JsonElement ParseJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallback = JsonDocument.Parse("[]");
            return fallback.RootElement.Clone();
        }
    }
}
