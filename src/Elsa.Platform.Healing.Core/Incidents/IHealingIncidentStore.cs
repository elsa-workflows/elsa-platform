namespace Elsa.Platform.Healing.Core.Incidents;

public sealed record IncidentAttributionDraft(
    Guid ComponentEntryId,
    Guid? BindingId,
    decimal Confidence,
    AttributionBasis Basis,
    AttributionResolution Resolution,
    IReadOnlyList<string> ReasonCodes);

public sealed record HealingIncidentProjectionRequest(
    Guid InboxItemId,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid? RevisionId,
    string OccurrenceKey,
    DateTimeOffset OccurredAt,
    DateTimeOffset AcceptedAt,
    IncidentClassification Classification,
    IncidentSeverity Severity,
    string ExceptionType,
    string OperationName,
    string NormalizedStackJson,
    string? TraceId,
    string? SpanId,
    IncidentRetryState RetryState,
    string FingerprintVersion,
    string Fingerprint,
    EvidenceTier EvidenceTier,
    string EvidenceDigest,
    string RepairRepositoryKey,
    Guid? SelectedBindingId,
    Guid? SelectedComponentEntryId,
    Guid? ProviderConnectionId,
    IReadOnlyList<IncidentAttributionDraft> Attributions,
    int OccurrenceThreshold,
    TimeSpan DebounceWindow,
    string ClassificationPolicyVersion,
    string ClassificationPolicyHash);

public sealed record HealingIncidentProjectionResult(
    IncidentOccurrence Occurrence,
    HealingIncident Incident,
    IncidentEpisode Episode,
    EnvironmentImpact EnvironmentImpact,
    bool IsReplay,
    bool IsRegression);

public interface IHealingIncidentStore
{
    ValueTask<HealingIncidentProjectionResult> ProjectOccurrenceAsync(
        HealingIncidentProjectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<int> PromoteDueIncidentsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingIncident>> ListIncidentsAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingIncident?> GetIncidentAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        CancellationToken cancellationToken = default);
}
