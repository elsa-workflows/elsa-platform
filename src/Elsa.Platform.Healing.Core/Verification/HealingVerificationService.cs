using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Security;

namespace Elsa.Platform.Healing.Core.Verification;

public sealed class HealingVerificationService(
    IHealingVerificationStore store,
    TimeProvider timeProvider,
    HealingAuditService? auditService = null,
    IRepairVerificationSignalSink? failureSignalSink = null)
{
    private static readonly IReadOnlySet<VerificationOutcome> TerminalOutcomes =
        new HashSet<VerificationOutcome>
        {
            VerificationOutcome.Healed,
            VerificationOutcome.Superseded,
            VerificationOutcome.Waived
        };

    public async ValueTask ObserveDeploymentAsync(
        DeploymentObservation observation,
        CancellationToken cancellationToken = default)
    {
        var scopes = await store.ListDeploymentScopesAsync(
            observation.WorkspaceId,
            observation.ApplicationId,
            observation.EnvironmentId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var candidate in scopes)
        {
            var scope = candidate;
            if (string.Equals(scope.RepairedRevision, observation.Revision, StringComparison.OrdinalIgnoreCase))
            {
                if (scope.Verification?.Outcome == VerificationOutcome.Superseded)
                    continue;
                var verification = scope.Verification ?? NewVerification(scope, observation);
                if (scope.Verification is null)
                {
                    verification = await store.UpsertVerificationAsync(verification, cancellationToken);
                    scope = scope with { Verification = verification };
                }
                if (verification.Outcome is VerificationOutcome.PendingDeployment or VerificationOutcome.Deployed)
                    verification.Outcome = VerificationOutcome.DeployedUnverified;
                verification.DeploymentObservationId = observation.Id;
                verification.WindowStartedAt ??= observation.DeployedAt;
                verification.WindowEndsAt ??= observation.DeployedAt.Add(scope.Configuration.VerificationWindow);
                scope.EnvironmentImpact.CurrentDeployedRevision = observation.Revision;
                scope.EnvironmentImpact.VerificationStatus = verification.Outcome;
                if (scope.Incident.Status is HealingIncidentStatus.Merged or HealingIncidentStatus.PullRequestOpen)
                    scope.Incident.Status = HealingIncidentStatus.Verifying;
                continue;
            }

            var current = scope.Verification;
            if (current?.WindowStartedAt is not null && observation.DeployedAt >= current.WindowStartedAt && !TerminalOutcomes.Contains(current.Outcome))
            {
                current.Outcome = VerificationOutcome.Superseded;
                current.DecidedAt = now;
                current.SafeDecisionReason = "later-deployment-observed";
                scope.EnvironmentImpact.CurrentDeployedRevision = observation.Revision;
                scope.EnvironmentImpact.VerificationStatus = VerificationOutcome.Superseded;
                scope.EnvironmentImpact.ClosedAt = now;
                scope.EnvironmentImpact.ClosedByActorId = "deployment-system";
            }
        }
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await store.SaveAsync(transactionCancellationToken);
            foreach (var episodeId in scopes.Select(x => x.Episode.Id).Distinct())
                await CloseIncidentIfCompleteAsync(
                    observation.WorkspaceId, observation.ApplicationId, episodeId, transactionCancellationToken);
            return true;
        }, cancellationToken);
    }

    public async ValueTask<bool> RecordEpisodePositiveOperationAsync(
        Guid workspaceId,
        Guid episodeId,
        Guid environmentId,
        string repairedRevision,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var scope = await store.GetScopeAsync(workspaceId, episodeId, environmentId, repairedRevision, cancellationToken);
        return await RecordPositiveOperationAsync(scope, observedAt, cancellationToken);
    }

    public async ValueTask<bool> RecordPositiveOperationAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string repairedRevision,
        string operationName,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var scope = await store.FindActiveScopeAsync(
            workspaceId, applicationId, environmentId, repairedRevision, operationName, cancellationToken);
        return await RecordPositiveOperationAsync(scope, observedAt, cancellationToken);
    }

    private async ValueTask<bool> RecordPositiveOperationAsync(
        HealingVerificationScope? scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (scope?.Verification is not { } verification ||
            verification.Outcome is not (VerificationOutcome.Deployed or VerificationOutcome.DeployedUnverified))
            return false;
        var now = timeProvider.GetUtcNow();
        if (verification.WindowStartedAt is null || verification.WindowEndsAt is null ||
            observedAt < verification.WindowStartedAt || observedAt > verification.WindowEndsAt || observedAt > now)
            return false;
        if (verification.LastRelevantOperationSuccessAt is { } lastObservedAt && observedAt <= lastObservedAt)
            return true;
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var firstPositiveObservation = verification.RelevantOperationSuccessCount == 0;
            verification.RelevantOperationSuccessCount++;
            verification.LastRelevantOperationSuccessAt = observedAt;
            await EvaluateWindowAsync(scope, now);
            await store.SaveAsync(transactionCancellationToken);
            if (firstPositiveObservation)
                await AuditAsync(scope, "positive-operation-observed", "affected-operation-succeeded", HealingActorTypes.DeploymentSystem,
                    "authenticated-telemetry-source", scope.Verification!.Id, transactionCancellationToken);
            await CloseIncidentIfCompleteAsync(
                scope.Incident.WorkspaceId, scope.Incident.ApplicationId, scope.Episode.Id, transactionCancellationToken);
            return true;
        }, cancellationToken);
        return true;
    }

    public async ValueTask<HealingVerificationScope?> RecordRecurrenceAsync(
        IncidentOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        return await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var scope = await store.FindScopeForOccurrenceAsync(occurrence, transactionCancellationToken);
            if (scope?.Verification is not { } verification || TerminalOutcomes.Contains(verification.Outcome))
                return null;
            var replay = verification.Outcome == VerificationOutcome.FailedVerification &&
                         verification.SupportingOccurrenceId == occurrence.Id;
            var now = timeProvider.GetUtcNow();
            if (!replay && (verification.WindowStartedAt is null || verification.WindowEndsAt is null ||
                            occurrence.OccurredAt < verification.WindowStartedAt || occurrence.OccurredAt > verification.WindowEndsAt ||
                            occurrence.OccurredAt > now))
                return null;
            if (!replay)
            {
                verification.RecurrenceCount++;
                verification.LastRecurrenceAt = occurrence.OccurredAt;
                verification.SupportingOccurrenceId = occurrence.Id;
                verification.Outcome = VerificationOutcome.FailedVerification;
                verification.DecidedAt = now;
                verification.SafeDecisionReason = "matching-recurrence";
                scope.EnvironmentImpact.VerificationStatus = VerificationOutcome.FailedVerification;
                scope.Incident.Status = HealingIncidentStatus.FailedVerification;
                scope.Incident.NeedsHumanReason = NeedsHumanReason.VerificationFailed;
                scope.Episode.Outcome = IncidentEpisodeOutcome.Failed;
                scope.Episode.ClosedAt = verification.DecidedAt;
                await store.SaveAsync(transactionCancellationToken);
                await AuditAsync(scope, "verification-failed", "matching-recurrence", HealingActorTypes.Platform,
                    "healing-verification-worker", verification.Id, transactionCancellationToken);
            }
            if (failureSignalSink is not null)
                await failureSignalSink.AppendAsync(
                    FailureSignal(scope, occurrence.Id, verification.DecidedAt ?? now),
                    transactionCancellationToken);
            return scope;
        }, cancellationToken);
    }

    private static RepairVerificationFailedSignal FailureSignal(
        HealingVerificationScope scope,
        Guid occurrenceId,
        DateTimeOffset detectedAt) => new(
        HealingContractVersions.DeploymentProtocol,
        scope.Incident.WorkspaceId,
        scope.Incident.ApplicationId,
        scope.EnvironmentImpact.EnvironmentId,
        scope.Incident.Id,
        scope.Episode.Id,
        scope.RepairedRevision,
        occurrenceId,
        "matching-recurrence",
        detectedAt);

    public async ValueTask<bool> EvaluateDueAsync(
        HealingVerificationScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (scope.Verification is null || scope.Verification.WindowEndsAt is null || scope.Verification.WindowEndsAt > now)
            return false;
        var changed = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            if (!await EvaluateWindowAsync(scope, now))
                return false;
            await store.SaveAsync(transactionCancellationToken);
            await AuditAsync(scope, "environment-healed", "positive-operation-and-window-complete", HealingActorTypes.Platform,
                "healing-verification-worker", scope.Verification.Id, transactionCancellationToken);
            await CloseIncidentIfCompleteAsync(
                scope.Incident.WorkspaceId, scope.Incident.ApplicationId, scope.Episode.Id, transactionCancellationToken);
            return true;
        }, cancellationToken);
        if (!changed)
            return false;
        return true;
    }

    public async ValueTask<bool> WaiveAsync(
        Guid workspaceId,
        Guid episodeId,
        Guid environmentId,
        string repairedRevision,
        string actorId,
        string reason,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Trim().Length > 256 || actorId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1_024 || reason.Any(char.IsControl))
            throw new ArgumentException("Waiver actor and reason are required.");
        var scope = await store.GetScopeAsync(workspaceId, episodeId, environmentId, repairedRevision, cancellationToken);
        if (scope?.Verification is not { } verification || TerminalOutcomes.Contains(verification.Outcome))
            return false;
        var now = timeProvider.GetUtcNow();
        if (expiresAt is not null && expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Waiver expiry must be in the future.");
        if (expiresAt > now.AddDays(365))
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Temporary waiver expiry cannot exceed 365 days.");
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            verification.Outcome = VerificationOutcome.Waived;
            verification.DecidedAt = now;
            verification.SafeDecisionReason = reason.Trim();
            verification.WaiverExpiresAt = expiresAt;
            scope.EnvironmentImpact.VerificationStatus = VerificationOutcome.Waived;
            scope.EnvironmentImpact.ClosedAt = now;
            scope.EnvironmentImpact.ClosedByActorId = actorId.Trim();
            await store.SaveAsync(transactionCancellationToken);
            await AuditAsync(scope, "verification-waived", "authorized-waiver", HealingActorTypes.Human, actorId,
                Guid.NewGuid(), transactionCancellationToken);
            await CloseIncidentIfCompleteAsync(
                scope.Incident.WorkspaceId, scope.Incident.ApplicationId, scope.Episode.Id, transactionCancellationToken);
            return true;
        }, cancellationToken);
        return true;
    }

    public async ValueTask<bool> ExpireWaiverAsync(
        HealingVerificationScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (scope.Verification is not { Outcome: VerificationOutcome.Waived, WaiverExpiresAt: { } expiry } verification || expiry > now)
            return false;
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var hasDeployment = verification.DeploymentObservationId.HasValue;
            verification.Outcome = hasDeployment
                ? VerificationOutcome.DeployedUnverified
                : VerificationOutcome.PendingDeployment;
            verification.DecidedAt = null;
            verification.SafeDecisionReason = "waiver-expired";
            verification.WaiverExpiresAt = null;
            verification.WindowStartedAt = hasDeployment ? now : null;
            verification.WindowEndsAt = hasDeployment ? now.Add(scope.Configuration.VerificationWindow) : null;
            verification.RelevantOperationSuccessCount = 0;
            verification.LastRelevantOperationSuccessAt = null;
            verification.RecurrenceCount = 0;
            verification.LastRecurrenceAt = null;
            verification.SupportingOccurrenceId = null;
            scope.EnvironmentImpact.VerificationStatus = verification.Outcome;
            scope.EnvironmentImpact.ClosedAt = null;
            scope.EnvironmentImpact.ClosedByActorId = null;
            scope.Incident.Status = HealingIncidentStatus.Verifying;
            scope.Episode.Outcome = IncidentEpisodeOutcome.Active;
            scope.Episode.ClosedAt = null;
            await store.SaveAsync(transactionCancellationToken);
            await AuditAsync(scope, "verification-waiver-expired", "waiver-expired", HealingActorTypes.Platform,
                "healing-verification-worker", Guid.NewGuid(), transactionCancellationToken);
            return true;
        }, cancellationToken);
        return true;
    }

    private static VerificationResult NewVerification(HealingVerificationScope scope, DeploymentObservation observation)
    {
        var verification = new VerificationResult
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scope.Incident.WorkspaceId,
            ApplicationId = scope.Incident.ApplicationId,
            EpisodeId = scope.Episode.Id,
            EnvironmentId = scope.EnvironmentImpact.EnvironmentId,
            RepairedRevision = scope.RepairedRevision,
            WindowStartedAt = observation.DeployedAt,
            WindowEndsAt = observation.DeployedAt.Add(scope.Configuration.VerificationWindow),
            Outcome = VerificationOutcome.DeployedUnverified,
            DeploymentObservationId = observation.Id
        };
        scope.EnvironmentImpact.VerificationStatus = VerificationOutcome.DeployedUnverified;
        return verification;
    }

    private static ValueTask<bool> EvaluateWindowAsync(HealingVerificationScope scope, DateTimeOffset now)
    {
        var verification = scope.Verification!;
        if (verification.RecurrenceCount > 0)
            return ValueTask.FromResult(false);
        if (verification.WindowEndsAt is null || verification.WindowEndsAt > now || verification.RelevantOperationSuccessCount == 0)
        {
            if (verification.Outcome == VerificationOutcome.Deployed)
                verification.Outcome = VerificationOutcome.DeployedUnverified;
            scope.EnvironmentImpact.VerificationStatus = verification.Outcome;
            return ValueTask.FromResult(false);
        }
        verification.Outcome = VerificationOutcome.Healed;
        verification.DecidedAt = now;
        verification.SafeDecisionReason = "positive-operation-and-window-complete";
        scope.EnvironmentImpact.VerificationStatus = VerificationOutcome.Healed;
        scope.EnvironmentImpact.ClosedAt = now;
        scope.EnvironmentImpact.ClosedByActorId = "healing-verification-worker";
        return ValueTask.FromResult(true);
    }

    private async ValueTask CloseIncidentIfCompleteAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var scope = await store.GetEpisodeScopeAsync(workspaceId, applicationId, episodeId, cancellationToken);
        if (scope is null)
            return;
        var impacts = await store.ListEpisodeImpactsAsync(workspaceId, applicationId, episodeId, cancellationToken);
        if (impacts.Count == 0 || impacts.Any(x => !TerminalOutcomes.Contains(x.VerificationStatus)))
            return;
        var verifications = await store.ListEpisodeVerificationsAsync(workspaceId, applicationId, episodeId, cancellationToken);
        if (verifications.Any(x => x.Outcome == VerificationOutcome.Waived && x.WaiverExpiresAt is not null))
            return;
        var now = timeProvider.GetUtcNow();
        string closureEvent;
        string closureReason;
        if (impacts.All(x => x.VerificationStatus == VerificationOutcome.Healed))
        {
            scope.Incident.Status = HealingIncidentStatus.Healed;
            scope.Episode.Outcome = IncidentEpisodeOutcome.Healed;
            closureEvent = "incident-healed";
            closureReason = "all-environments-healed";
        }
        else if (impacts.Any(x => x.VerificationStatus == VerificationOutcome.Waived))
        {
            scope.Incident.Status = HealingIncidentStatus.Waived;
            scope.Episode.Outcome = IncidentEpisodeOutcome.Waived;
            closureEvent = "incident-waived";
            closureReason = "environment-waiver-completed";
        }
        else
        {
            scope.Incident.Status = HealingIncidentStatus.Superseded;
            scope.Episode.Outcome = IncidentEpisodeOutcome.Superseded;
            closureEvent = "incident-superseded";
            closureReason = "all-environments-superseded";
        }
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            scope.Incident.NeedsHumanReason = null;
            scope.Episode.ClosedAt = now;
            await store.SaveAsync(transactionCancellationToken);
            if (auditService is not null)
                await auditService.AppendAsync(new HealingAuditWrite(
                scope.Incident.WorkspaceId,
                "healing-incident",
                scope.Incident.Id,
                closureEvent,
                closureReason,
                HealingActorTypes.Platform,
                "healing-verification-worker",
                scope.Episode.Id,
                null,
                null,
                null,
                null,
                new Dictionary<string, string?>
                {
                    ["status"] = scope.Incident.Status.ToString()
                }), transactionCancellationToken);
            return true;
        }, cancellationToken);
    }

    private ValueTask<HealingAuditEvent?> AuditAsync(
        HealingVerificationScope scope,
        string eventType,
        string reasonCode,
        string actorType,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (auditService is null || scope.Verification is null)
            return ValueTask.FromResult<HealingAuditEvent?>(null);
        var revision = scope.RepairedRevision.Length is >= 7 and <= 64 && scope.RepairedRevision.All(char.IsAsciiHexDigit)
            ? scope.RepairedRevision
            : null;
        return AuditCoreAsync();

        async ValueTask<HealingAuditEvent?> AuditCoreAsync() =>
            await auditService.AppendAsync(new HealingAuditWrite(
                scope.Incident.WorkspaceId,
                "verification-result",
                scope.Verification.Id,
                eventType,
                reasonCode,
                actorType,
                actorId,
                correlationId,
                null,
                null,
                null,
                null,
                new Dictionary<string, string?>
                {
                    ["environment"] = scope.EnvironmentImpact.EnvironmentId.ToString("D"),
                    ["revision"] = revision,
                    ["verificationStatus"] = scope.Verification.Outcome.ToString()
                }), cancellationToken);
    }
}
