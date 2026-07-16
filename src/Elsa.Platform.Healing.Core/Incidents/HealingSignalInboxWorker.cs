using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Security;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Healing.Core.Incidents;

public enum HealingInboxWorkerStatus { Idle, Projected, Rejected, RetryScheduled, DeadLettered, LeaseLost }

public sealed record HealingInboxWorkerResult(
    HealingInboxWorkerStatus Status,
    Guid? InboxItemId = null,
    Guid? IncidentId = null,
    string? OutcomeCode = null);

public sealed class HealingSignalInboxWorker(
    IHealingSignalInboxStore inboxStore,
    IHealingOwnershipStore ownershipStore,
    HealingSignalNormalizer normalizer,
    HealingSignalClassifier classifier,
    ComponentAttributionService attributionService,
    HealingFingerprintService fingerprintService,
    HealingIncidentService incidentService,
    HealingAuditService auditService,
    HealingKillSwitch killSwitch,
    IOptions<HealingOptions> options,
    TimeProvider timeProvider)
{
    public const int MaximumAttempts = 3;
    private readonly HealingOptions _options = options.Value;

    public ValueTask<int> PromoteDueIncidentsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        incidentService.PromoteDueAsync(now, batchSize, cancellationToken);

    public async ValueTask<HealingInboxWorkerResult> RunOnceAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = timeProvider.GetUtcNow();
        var lease = await inboxStore.TryLeaseNextAsync(workerId, now, _options.LeaseDuration, cancellationToken);
        if (lease is null)
            return new HealingInboxWorkerResult(HealingInboxWorkerStatus.Idle);

        try
        {
            return await ProcessAsync(lease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var finishedAt = timeProvider.GetUtcNow();
            if (lease.Item.AttemptCount >= MaximumAttempts)
            {
                var completed = await inboxStore.CompleteAsync(
                    lease.Item.Id,
                    lease.LeaseToken,
                    finishedAt,
                    HealingInboxStatus.DeadLettered,
                    "processing-attempt-limit",
                    "Incident projection exhausted its bounded retry limit.",
                    cancellationToken);
                if (!completed)
                    return LeaseLost(lease.Item.Id);
                return new HealingInboxWorkerResult(
                    HealingInboxWorkerStatus.DeadLettered,
                    lease.Item.Id,
                    OutcomeCode: "processing-attempt-limit");
            }

            var retryScheduled = await inboxStore.RetryAsync(
                lease.Item.Id,
                lease.LeaseToken,
                finishedAt,
                finishedAt.Add(_options.RetryDelay),
                "processing-failed",
                "Incident projection will be retried.",
                cancellationToken);
            if (!retryScheduled)
                return LeaseLost(lease.Item.Id);
            return new HealingInboxWorkerResult(
                HealingInboxWorkerStatus.RetryScheduled,
                lease.Item.Id,
                OutcomeCode: "processing-failed");
        }
    }

    private static HealingInboxWorkerResult LeaseLost(Guid itemId) => new(
        HealingInboxWorkerStatus.LeaseLost,
        itemId,
        OutcomeCode: "lease-lost");

    private async ValueTask<HealingInboxWorkerResult> ProcessAsync(
        HealingInboxLease lease,
        CancellationToken cancellationToken)
    {
        var item = lease.Item;
        var signal = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson);
        if (signal is null || signal.ApplicationId != item.ApplicationId || signal.EnvironmentId != item.EnvironmentId)
            return await RejectAsync(lease, "scope-mismatch", cancellationToken);

        var normalization = normalizer.Normalize(signal);
        if (!normalization.Succeeded)
            return await RejectAsync(lease, normalization.ReasonCodes.FirstOrDefault() ?? "normalization-rejected", cancellationToken);

        var configuration = await ownershipStore.GetConfigurationAsync(
            item.WorkspaceId,
            item.ApplicationId,
            cancellationToken);
        if (configuration is null)
            return await RejectAsync(lease, "configuration-not-found", cancellationToken);
        var environment = configuration.Environments.SingleOrDefault(x => x.EnvironmentId == item.EnvironmentId);
        var workspace = await ownershipStore.GetWorkspaceConfigurationAsync(item.WorkspaceId, cancellationToken);
        if (workspace is null)
            return await RejectAsync(lease, HealingGateReasonCodes.WorkspaceConfigurationNotFound, cancellationToken);
        var discoveryGate = killSwitch.CanDiscover(workspace, configuration, environment);
        if (!discoveryGate.Allowed)
            return await RejectAsync(lease, discoveryGate.ReasonCode, cancellationToken);
        var policyJson = environment is not null && environment.ClassificationPolicyJson != "{}"
            ? environment.ClassificationPolicyJson
            : configuration.ClassificationPolicyJson;
        var policy = ParsePolicy(
            policyJson,
            normalization.Signal!.FailureClass,
            environment?.OccurrenceThreshold ?? 1,
            environment?.DebounceWindow ?? TimeSpan.Zero);
        var classification = classifier.Classify(normalization.Signal!, policy.Override);
        if (!classification.IsEligible || classification.Classification is null)
            return await RejectAsync(lease, classification.ReasonCode, cancellationToken);

        var attribution = await attributionService.AttributeAsync(item.WorkspaceId, normalization.Signal!, cancellationToken);
        var componentCandidates = attribution.Candidates.Select(x => x.Component.ComponentKey);
        var fingerprint = fingerprintService.Compute(
            normalization.Signal!,
            componentCandidates,
            attribution.RepairRepositoryKey);
        var projection = await incidentService.ProjectAsync(new HealingIncidentCandidate(
            item.Id,
            item.WorkspaceId,
            item.AcceptedAt,
            normalization.Signal!,
            classification.Classification.Value,
            fingerprint,
            attribution.RepairRepositoryKey,
            attribution.SelectedBinding?.Id,
            attribution.SelectedComponent?.Id,
            attribution.SelectedProvider?.Id,
            attribution.Candidates.Select(candidate => new IncidentAttributionDraft(
                candidate.Component.Id,
                candidate.Binding?.Id,
                candidate.Confidence,
                candidate.Basis,
                candidate.Resolution,
                candidate.ReasonCodes)).ToArray(),
            HealingIncidentPolicy.Create(
                policy.OccurrenceThreshold,
                policy.DebounceWindow,
                policy.Version,
                policyJson)), cancellationToken);

        var completedAt = timeProvider.GetUtcNow();
        await AuditAsync(
            item.WorkspaceId,
            projection.Incident.Id,
            projection.IsReplay ? "occurrence-deduplicated" : "occurrence-projected",
            projection.IsRegression ? "regression-episode" : "accepted",
            item.Id,
            cancellationToken);
        if (!await inboxStore.CompleteAsync(
                item.Id,
                lease.LeaseToken,
                completedAt,
                HealingInboxStatus.Completed,
                projection.IsReplay ? "occurrence-replayed" : "incident-projected",
                null,
                cancellationToken))
            throw new InvalidOperationException("The inbox lease expired before completion.");
        return new HealingInboxWorkerResult(
            HealingInboxWorkerStatus.Projected,
            item.Id,
            projection.Incident.Id,
            projection.IsReplay ? "occurrence-replayed" : "incident-projected");
    }

    private async ValueTask<HealingInboxWorkerResult> RejectAsync(
        HealingInboxLease lease,
        string outcomeCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await AuditAsync(
            lease.Item.WorkspaceId,
            lease.Item.Id,
            "candidate-rejected",
            outcomeCode,
            lease.Item.Id,
            cancellationToken);
        if (!await inboxStore.CompleteAsync(
                lease.Item.Id,
                lease.LeaseToken,
                now,
                HealingInboxStatus.Rejected,
                outcomeCode,
                null,
                cancellationToken))
            throw new InvalidOperationException("The inbox lease expired before rejection was persisted.");
        return new HealingInboxWorkerResult(HealingInboxWorkerStatus.Rejected, lease.Item.Id, OutcomeCode: outcomeCode);
    }

    private ValueTask<HealingAuditEvent> AuditAsync(
        Guid workspaceId,
        Guid aggregateId,
        string eventType,
        string reasonCode,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        auditService.AppendAsync(new HealingAuditWrite(
            workspaceId,
            "healing-incident",
            aggregateId,
            eventType,
            reasonCode,
            HealingActorTypes.Platform,
            "healing-inbox-worker",
            correlationId,
            null,
            null,
            null,
            null,
            new Dictionary<string, string?> { ["outcomeCode"] = reasonCode }), cancellationToken);

    private static ClassificationPolicy ParsePolicy(
        string policyJson,
        string failureClass,
        int defaultThreshold,
        TimeSpan defaultDebounce)
    {
        try
        {
            using var document = JsonDocument.Parse(policyJson);
            var root = document.RootElement;
            var version = root.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString() ?? "1"
                : "1";
            var threshold = defaultThreshold;
            if (root.TryGetProperty("thresholds", out var thresholds) &&
                thresholds.ValueKind == JsonValueKind.Object &&
                thresholds.TryGetProperty(failureClass, out var thresholdElement) &&
                thresholdElement.TryGetInt32(out var configuredThreshold) && configuredThreshold >= 1)
                threshold = configuredThreshold;
            var debounce = defaultDebounce;
            if (root.TryGetProperty("debounceSeconds", out var debounceElement) &&
                debounceElement.TryGetInt32(out var debounceSeconds) && debounceSeconds >= 0)
                debounce = TimeSpan.FromSeconds(debounceSeconds);
            HealingClassificationOverride? policyOverride = null;
            if (root.TryGetProperty("overrides", out var overrides) &&
                overrides.ValueKind == JsonValueKind.Object &&
                overrides.TryGetProperty(failureClass, out var overrideElement) &&
                overrideElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(overrideElement.GetString()))
                policyOverride = new HealingClassificationOverride(overrideElement.GetString()!, IsAuthorized: true);
            return new ClassificationPolicy(version, threshold, debounce, policyOverride);
        }
        catch (JsonException)
        {
            return new ClassificationPolicy("invalid", defaultThreshold, defaultDebounce, null);
        }
    }

    private sealed record ClassificationPolicy(
        string Version,
        int OccurrenceThreshold,
        TimeSpan DebounceWindow,
        HealingClassificationOverride? Override);
}
