using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Verification;
using Elsa.Platform.Healing.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingVerificationStore(
    HealingDbContext dbContext,
    HealingStore healingStore) : IHealingVerificationStore, IRepairVerificationFailedSignalOutbox
{
    public ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default) =>
        healingStore.ExecuteInTransactionAsync(operation, cancellationToken);

    public async ValueTask<HealingVerificationAppendResult<DeploymentObservation>> AppendDeploymentObservationAsync(
        DeploymentObservation observation,
        CancellationToken cancellationToken = default)
    {
        var result = await healingStore.AppendDeploymentObservationAsync(observation, cancellationToken);
        return new HealingVerificationAppendResult<DeploymentObservation>(result.Value, result.IsReplay);
    }

    public ValueTask<VerificationResult> UpsertVerificationAsync(
        VerificationResult verification,
        CancellationToken cancellationToken = default) =>
        healingStore.UpsertVerificationAsync(verification, cancellationToken);

    public async ValueTask<IReadOnlyList<HealingVerificationScope>> ListDeploymentScopesAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken = default)
    {
        var roots = await (
            from impact in dbContext.EnvironmentImpacts
            join episode in dbContext.IncidentEpisodes on new { impact.WorkspaceId, impact.ApplicationId, Id = impact.EpisodeId }
                equals new { episode.WorkspaceId, episode.ApplicationId, episode.Id }
            join incident in dbContext.HealingIncidents on new { episode.WorkspaceId, episode.ApplicationId, Id = episode.IncidentId }
                equals new { incident.WorkspaceId, incident.ApplicationId, incident.Id }
            join configuration in dbContext.HealingConfigurations on new { incident.WorkspaceId, incident.ApplicationId }
                equals new { configuration.WorkspaceId, configuration.ApplicationId }
            where impact.WorkspaceId == workspaceId && impact.ApplicationId == applicationId &&
                  impact.EnvironmentId == environmentId && incident.ActiveEpisodeId == episode.Id &&
                  (incident.Status == HealingIncidentStatus.Merged || incident.Status == HealingIncidentStatus.Verifying ||
                   incident.Status == HealingIncidentStatus.FailedVerification)
            select new { incident, episode, impact, configuration })
            .ToArrayAsync(cancellationToken);

        var scopes = new List<HealingVerificationScope>(roots.Length);
        foreach (var root in roots)
        {
            var repairedRevision = await GetMergedRevisionAsync(
                workspaceId, applicationId, root.incident.Id, root.episode.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(repairedRevision))
                continue;
            var verification = await dbContext.VerificationResults.SingleOrDefaultAsync(x =>
                x.WorkspaceId == workspaceId && x.ApplicationId == applicationId &&
                x.EpisodeId == root.episode.Id && x.EnvironmentId == environmentId &&
                x.RepairedRevision == repairedRevision, cancellationToken);
            scopes.Add(new HealingVerificationScope(
                root.incident, root.episode, root.impact, root.configuration, repairedRevision, verification));
        }
        return scopes;
    }

    public async ValueTask<HealingVerificationScope?> GetScopeAsync(
        Guid workspaceId,
        Guid episodeId,
        Guid environmentId,
        string repairedRevision,
        CancellationToken cancellationToken = default)
    {
        var verification = await dbContext.VerificationResults.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.EpisodeId == episodeId && x.EnvironmentId == environmentId &&
            x.RepairedRevision == repairedRevision, cancellationToken);
        if (verification is null)
            return null;
        return await LoadScopeAsync(verification, cancellationToken);
    }

    public async ValueTask<HealingVerificationScope?> GetEpisodeScopeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default)
    {
        var verification = await dbContext.VerificationResults.FirstOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EpisodeId == episodeId,
            cancellationToken);
        if (verification is not null)
            return await LoadScopeAsync(verification, cancellationToken);

        var episode = await dbContext.IncidentEpisodes.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == episodeId, cancellationToken);
        if (episode is null)
            return null;
        var incident = await dbContext.HealingIncidents.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == episode.IncidentId, cancellationToken);
        var impact = await dbContext.EnvironmentImpacts.FirstOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EpisodeId == episodeId, cancellationToken);
        var configuration = await dbContext.HealingConfigurations.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId, cancellationToken);
        if (impact is null || configuration is null)
            return null;
        return new HealingVerificationScope(incident, episode, impact, configuration,
            await GetMergedRevisionAsync(workspaceId, applicationId, incident.Id, episode.Id, cancellationToken) ?? string.Empty, null);
    }

    public async ValueTask<HealingVerificationScope?> FindActiveScopeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string repairedRevision,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        repairedRevision = repairedRevision.Trim().ToLowerInvariant();
        var verification = await (from result in dbContext.VerificationResults
                                  join episode in dbContext.IncidentEpisodes on new { result.WorkspaceId, result.ApplicationId, Id = result.EpisodeId }
                                      equals new { episode.WorkspaceId, episode.ApplicationId, episode.Id }
                                  join incident in dbContext.HealingIncidents on new { episode.WorkspaceId, episode.ApplicationId, Id = episode.IncidentId }
                                      equals new { incident.WorkspaceId, incident.ApplicationId, incident.Id }
                                  where result.WorkspaceId == workspaceId && result.ApplicationId == applicationId &&
                                        result.EnvironmentId == environmentId && result.RepairedRevision == repairedRevision &&
                                        incident.ActiveEpisodeId == episode.Id && incident.Status == HealingIncidentStatus.Verifying &&
                                        dbContext.IncidentOccurrences.Any(occurrence => occurrence.WorkspaceId == workspaceId &&
                                            occurrence.ApplicationId == applicationId && occurrence.IncidentId == incident.Id &&
                                            occurrence.EpisodeId == episode.Id && occurrence.EnvironmentId == environmentId &&
                                            occurrence.OperationName == operationName)
                                  select result).SingleOrDefaultAsync(cancellationToken);
        return verification is null ? null : await LoadScopeAsync(verification, cancellationToken);
    }

    public async ValueTask<HealingVerificationScope?> FindScopeForOccurrenceAsync(
        IncidentOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        var sourceRevision = occurrence.RevisionId is null ? null : await dbContext.ComponentManifests.AsNoTracking()
            .Where(x => x.WorkspaceId == occurrence.WorkspaceId && x.ApplicationId == occurrence.ApplicationId &&
                        x.RevisionId == occurrence.RevisionId)
            .Select(x => x.SourceRevision)
            .SingleOrDefaultAsync(cancellationToken);
        var result = await dbContext.VerificationResults.SingleOrDefaultAsync(x =>
            x.WorkspaceId == occurrence.WorkspaceId && x.ApplicationId == occurrence.ApplicationId &&
            x.EpisodeId == occurrence.EpisodeId && x.EnvironmentId == occurrence.EnvironmentId &&
            (sourceRevision != null
                ? x.RepairedRevision == sourceRevision.Trim().ToLower()
                : dbContext.EnvironmentImpacts.Any(impact => impact.WorkspaceId == occurrence.WorkspaceId &&
                    impact.ApplicationId == occurrence.ApplicationId && impact.EpisodeId == occurrence.EpisodeId &&
                    impact.EnvironmentId == occurrence.EnvironmentId && impact.CurrentDeployedRevision == x.RepairedRevision)),
            cancellationToken);
        return result is null ? null : await LoadScopeAsync(result, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<HealingVerificationScope>> ListDueScopesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default)
    {
        var verifications = await dbContext.VerificationResults
            .Where(x => x.WindowEndsAt != null && x.WindowEndsAt <= now &&
                        (x.Outcome == VerificationOutcome.Deployed || x.Outcome == VerificationOutcome.DeployedUnverified))
            .OrderBy(x => x.WindowEndsAt)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToArrayAsync(cancellationToken);
        var result = new List<HealingVerificationScope>(verifications.Length);
        foreach (var verification in verifications)
        {
            var scope = await LoadScopeAsync(verification, cancellationToken);
            if (scope is not null)
                result.Add(scope);
        }
        return result;
    }

    public async ValueTask<IReadOnlyList<HealingVerificationScope>> ListExpiredWaiverScopesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default)
    {
        var verifications = await dbContext.VerificationResults
            .Where(x => x.Outcome == VerificationOutcome.Waived && x.WaiverExpiresAt != null && x.WaiverExpiresAt <= now)
            .OrderBy(x => x.WaiverExpiresAt)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToArrayAsync(cancellationToken);
        var result = new List<HealingVerificationScope>(verifications.Length);
        foreach (var verification in verifications)
        {
            var scope = await LoadScopeAsync(verification, cancellationToken);
            if (scope is not null)
                result.Add(scope);
        }
        return result;
    }

    public async ValueTask<IReadOnlyList<EnvironmentImpact>> ListEpisodeImpactsAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default) =>
        await dbContext.EnvironmentImpacts.Where(x =>
                x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EpisodeId == episodeId)
            .OrderBy(x => x.EnvironmentId)
            .ToArrayAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<VerificationResult>> ListEpisodeVerificationsAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default) =>
        await dbContext.VerificationResults.Where(x =>
                x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EpisodeId == episodeId)
            .ToArrayAsync(cancellationToken);

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SaveChangesAsync(cancellationToken);

    public async ValueTask<RepairVerificationFailedSignalAppendReceipt> AppendAsync(
        RepairVerificationFailedSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!string.Equals(signal.ProtocolVersion, HealingContractVersions.DeploymentProtocol, StringComparison.Ordinal) ||
            signal.WorkspaceId == Guid.Empty || signal.ApplicationId == Guid.Empty || signal.EnvironmentId == Guid.Empty ||
            signal.IncidentId == Guid.Empty || signal.EpisodeId == Guid.Empty || signal.SupportingOccurrenceId == Guid.Empty)
            throw new ArgumentException("The verification failure signal is invalid.", nameof(signal));

        var idempotencyKey = $"verification-failed:{signal.EpisodeId:N}:{signal.EnvironmentId:N}:{signal.SupportingOccurrenceId:N}";
        var payload = JsonSerializer.Serialize(signal);
        // DetectedAt records when a particular projection attempt noticed the recurrence. It is not part
        // of the logical signal identity and can legitimately vary when the same occurrence is replayed.
        var canonicalPayload = JsonSerializer.Serialize(signal with { DetectedAt = default });
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
        var existing = await dbContext.RepairVerificationFailureOutbox.SingleOrDefaultAsync(x =>
            x.WorkspaceId == signal.WorkspaceId && x.ApplicationId == signal.ApplicationId &&
            x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new HealingIdempotencyConflictException("Verification failure signal identity was reused with different evidence.");
            return new RepairVerificationFailedSignalAppendReceipt(existing.Id, true, existing.CreatedAt);
        }

        var item = new RepairVerificationFailureOutboxItem
        {
            Id = Guid.NewGuid(), WorkspaceId = signal.WorkspaceId, ApplicationId = signal.ApplicationId,
            EnvironmentId = signal.EnvironmentId, IncidentId = signal.IncidentId, EpisodeId = signal.EpisodeId,
            SupportingOccurrenceId = signal.SupportingOccurrenceId, IdempotencyKey = idempotencyKey,
            PayloadJson = payload, PayloadHash = payloadHash, Status = RepairVerificationFailureDeliveryStatus.Pending,
            CreatedAt = signal.DetectedAt, UpdatedAt = signal.DetectedAt, Version = Guid.NewGuid().ToByteArray()
        };
        dbContext.RepairVerificationFailureOutbox.Add(item);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RepairVerificationFailedSignalAppendReceipt(item.Id, false, item.CreatedAt);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(item).State = EntityState.Detached;
            existing = await dbContext.RepairVerificationFailureOutbox.AsNoTracking().SingleOrDefaultAsync(x =>
                x.WorkspaceId == signal.WorkspaceId && x.ApplicationId == signal.ApplicationId &&
                x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is null)
                throw;
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new HealingIdempotencyConflictException("Verification failure signal identity was reused with different evidence.");
            return new RepairVerificationFailedSignalAppendReceipt(existing.Id, true, existing.CreatedAt);
        }
    }

    public async ValueTask<RepairVerificationFailedSignalLease?> TryLeaseNextAsync(
        string consumerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var candidateIds = await dbContext.RepairVerificationFailureOutbox.AsNoTracking()
            .Where(x =>
                (x.Status == RepairVerificationFailureDeliveryStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                (x.Status == RepairVerificationFailureDeliveryStatus.Leased && x.LeaseExpiresAt < now))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.Id).Take(8).ToArrayAsync(cancellationToken);
        foreach (var id in candidateIds)
        {
            var leaseToken = Guid.NewGuid().ToString("N");
            var leaseExpiresAt = now.Add(leaseDuration);
            var changed = await dbContext.RepairVerificationFailureOutbox.Where(x => x.Id == id &&
                    ((x.Status == RepairVerificationFailureDeliveryStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                     (x.Status == RepairVerificationFailureDeliveryStatus.Leased && x.LeaseExpiresAt < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, RepairVerificationFailureDeliveryStatus.Leased)
                    .SetProperty(x => x.LeaseOwner, consumerId)
                    .SetProperty(x => x.LeaseToken, leaseToken)
                    .SetProperty(x => x.LeaseExpiresAt, leaseExpiresAt)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
            if (changed == 0)
                continue;
            var item = await dbContext.RepairVerificationFailureOutbox.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
            var signal = JsonSerializer.Deserialize<RepairVerificationFailedSignal>(item.PayloadJson)
                         ?? throw new InvalidOperationException("The durable verification failure signal is invalid.");
            return new RepairVerificationFailedSignalLease(item.Id, leaseToken, signal, item.AttemptCount, leaseExpiresAt);
        }
        return null;
    }

    public async ValueTask<bool> MarkDeliveredAsync(
        Guid deliveryId,
        string leaseToken,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.RepairVerificationFailureOutbox
            .Where(x => x.Id == deliveryId && x.Status == RepairVerificationFailureDeliveryStatus.Leased &&
                        x.LeaseToken == leaseToken && x.LeaseExpiresAt >= deliveredAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RepairVerificationFailureDeliveryStatus.Delivered)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.DeliveredAt, deliveredAt)
                .SetProperty(x => x.UpdatedAt, deliveredAt)
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken) == 1;

    public async ValueTask<bool> ReleaseAsync(
        Guid deliveryId,
        string leaseToken,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt,
        string outcomeCode,
        CancellationToken cancellationToken = default)
    {
        if (nextAttemptAt < now)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        ArgumentException.ThrowIfNullOrWhiteSpace(outcomeCode);
        return await dbContext.RepairVerificationFailureOutbox
            .Where(x => x.Id == deliveryId && x.Status == RepairVerificationFailureDeliveryStatus.Leased &&
                        x.LeaseToken == leaseToken && x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RepairVerificationFailureDeliveryStatus.Pending)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, nextAttemptAt)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken) == 1;
    }

    private async ValueTask<HealingVerificationScope?> LoadScopeAsync(
        VerificationResult verification,
        CancellationToken cancellationToken)
    {
        var episode = await dbContext.IncidentEpisodes.SingleOrDefaultAsync(x =>
            x.WorkspaceId == verification.WorkspaceId && x.ApplicationId == verification.ApplicationId &&
            x.Id == verification.EpisodeId, cancellationToken);
        if (episode is null)
            return null;
        var incident = await dbContext.HealingIncidents.SingleAsync(x =>
            x.WorkspaceId == verification.WorkspaceId && x.ApplicationId == verification.ApplicationId &&
            x.Id == episode.IncidentId, cancellationToken);
        var impact = await dbContext.EnvironmentImpacts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == verification.WorkspaceId && x.ApplicationId == verification.ApplicationId &&
            x.EpisodeId == verification.EpisodeId && x.EnvironmentId == verification.EnvironmentId,
            cancellationToken);
        var configuration = await dbContext.HealingConfigurations.SingleOrDefaultAsync(x =>
            x.WorkspaceId == verification.WorkspaceId && x.ApplicationId == verification.ApplicationId,
            cancellationToken);
        return impact is null || configuration is null
            ? null
            : new HealingVerificationScope(incident, episode, impact, configuration,
                verification.RepairedRevision, verification);
    }

    private async ValueTask<string?> GetMergedRevisionAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        Guid episodeId,
        CancellationToken cancellationToken) =>
        (await (from pullRequest in dbContext.RepairPullRequests
               join attempt in dbContext.RepairAttempts
                   on new { pullRequest.WorkspaceId, pullRequest.ApplicationId, Id = pullRequest.AttemptId }
                   equals new { attempt.WorkspaceId, attempt.ApplicationId, attempt.Id }
               where attempt.WorkspaceId == workspaceId && attempt.ApplicationId == applicationId &&
                     attempt.IncidentId == incidentId && attempt.EpisodeId == episodeId &&
                     pullRequest.MergeState == PullRequestMergeState.Merged && pullRequest.MergedRevision != null
               orderby pullRequest.MergedAt descending, pullRequest.Id descending
               select pullRequest.MergedRevision).FirstOrDefaultAsync(cancellationToken))?.Trim().ToLowerInvariant();
}
