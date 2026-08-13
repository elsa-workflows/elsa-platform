using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ValenceControl.Healing.Core.Incidents;

public sealed record HealingIncidentPolicy(
    int OccurrenceThreshold,
    TimeSpan DebounceWindow,
    string Version,
    string Hash)
{
    public static HealingIncidentPolicy Create(
        int occurrenceThreshold,
        TimeSpan debounceWindow,
        string version,
        string canonicalPolicyJson)
    {
        if (occurrenceThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(occurrenceThreshold));
        if (debounceWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounceWindow));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(canonicalPolicyJson);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPolicyJson)))
            .ToLowerInvariant();
        return new HealingIncidentPolicy(occurrenceThreshold, debounceWindow, version, $"sha256:{hash}");
    }
}

public sealed record HealingIncidentCandidate(
    Guid InboxItemId,
    Guid WorkspaceId,
    DateTimeOffset AcceptedAt,
    NormalizedHealingSignal Signal,
    IncidentClassification Classification,
    HealingFingerprint Fingerprint,
    string RepairRepositoryKey,
    Guid? SelectedBindingId,
    Guid? SelectedComponentEntryId,
    Guid? ProviderConnectionId,
    IReadOnlyList<IncidentAttributionDraft> Attributions,
    HealingIncidentPolicy Policy);

public sealed class HealingIncidentService(IHealingIncidentStore store)
{
    public ValueTask<int> PromoteDueAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        store.PromoteDueIncidentsAsync(now, batchSize, cancellationToken);

    public ValueTask<HealingIncidentProjectionResult> ProjectAsync(
        HealingIncidentCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var signal = candidate.Signal;
        var evidenceDigest = DigestEvidence(signal);
        var request = new HealingIncidentProjectionRequest(
            candidate.InboxItemId,
            candidate.WorkspaceId,
            signal.ApplicationId,
            signal.EnvironmentId,
            signal.RevisionId,
            signal.OccurrenceKey,
            signal.OccurredAt,
            candidate.AcceptedAt,
            candidate.Classification,
            signal.Severity,
            signal.ExceptionType,
            signal.OperationName,
            JsonSerializer.Serialize(signal.Frames),
            signal.Trace?.TraceId,
            signal.Trace?.SpanId,
            signal.RetryState,
            candidate.Fingerprint.Version,
            candidate.Fingerprint.Value,
            EvidenceTier.DefaultRedacted,
            evidenceDigest,
            candidate.RepairRepositoryKey,
            candidate.SelectedBindingId,
            candidate.SelectedComponentEntryId,
            candidate.ProviderConnectionId,
            candidate.Attributions,
            candidate.Policy.OccurrenceThreshold,
            candidate.Policy.DebounceWindow,
            candidate.Policy.Version,
            candidate.Policy.Hash);
        return store.ProjectOccurrenceAsync(request, cancellationToken);
    }

    private static string DigestEvidence(NormalizedHealingSignal signal)
    {
        var evidence = JsonSerializer.Serialize(new
        {
            signal.ExceptionType,
            signal.OperationName,
            signal.Frames,
            signal.Source.Evidence.IsRedacted,
            signal.Source.Evidence.IsTruncated,
            signal.Source.Evidence.OmittedFields
        });
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant()}";
    }
}
