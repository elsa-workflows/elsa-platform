using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

public enum ElsaInstanceSourceReleaseOutcome { Confirmed, RetryableFailure, Ambiguous }

public sealed record ElsaInstanceSourceReleaseResult(
    ElsaInstanceSourceReleaseOutcome Outcome, string DiagnosticCode,
    string? ProviderCorrelationId = null, string? EvidenceReference = null, string? EvidenceDigest = null)
{
    public ElsaInstanceSourceReleaseResult Validate()
    {
        var hasAnyEvidence = ProviderCorrelationId is not null || EvidenceReference is not null || EvidenceDigest is not null;
        var hasCompleteEvidence = ProviderCorrelationId is not null && EvidenceReference is not null && EvidenceDigest is not null;
        if (!Enum.IsDefined(Outcome) || string.IsNullOrWhiteSpace(DiagnosticCode) || DiagnosticCode.Length > 128 ||
            DiagnosticCode.Any(character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-')) ||
            hasAnyEvidence != hasCompleteEvidence ||
            (Outcome == ElsaInstanceSourceReleaseOutcome.Confirmed) != hasCompleteEvidence)
            throw new ArgumentException("Source release result is invalid.");
        if (ProviderCorrelationId is { } correlationId && EvidenceReference is { } evidenceReference &&
            EvidenceDigest is { } evidenceDigest)
        {
            if (correlationId.Length > 128 || correlationId.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/')))
                throw new ArgumentException("Provider correlation identifier is invalid.", nameof(ProviderCorrelationId));
            _ = new ElsaInstanceCleanupEvidence(evidenceReference, evidenceDigest);
        }
        return this;
    }
}

public sealed record ElsaInstanceMigrationSourceReleaseClaim(
    ElsaInstanceMigration Migration, Guid ClaimToken, int AttemptNumber, DateTimeOffset ClaimedUntil);

public interface IElsaInstanceMigrationSourceReleasePort
{
    Task<ElsaInstanceSourceReleaseResult> ReleaseAsync(
        Guid organizationId, Guid workspaceId, Guid instanceId, Guid migrationId,
        Guid operationId, int attemptNumber, string idempotencyKey,
        ElsaInstanceMigrationReleaseReference source, CancellationToken cancellationToken = default);
}

public interface IElsaInstanceMigrationSourceReleaseStore
{
    Task<ElsaInstanceMigrationSourceReleaseClaim?> TryClaimDueAsync(
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<ElsaInstanceMigrationWriteResult> CompleteAsync(
        ElsaInstanceMigrationSourceReleaseClaim claim, ElsaInstanceSourceReleaseResult result,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> RenewAsync(
        ElsaInstanceMigrationSourceReleaseClaim claim, DateTimeOffset now, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}

public sealed class ElsaInstanceMigrationSourceReleaseWorker(
    IElsaInstanceMigrationSourceReleaseStore store,
    IElsaInstanceMigrationSourceReleasePort releasePort,
    TimeProvider? timeProvider = null,
    TimeSpan? leaseDuration = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _leaseDuration = leaseDuration is null or { Ticks: > 1 }
        ? leaseDuration ?? TimeSpan.FromMinutes(5)
        : throw new ArgumentOutOfRangeException(nameof(leaseDuration));

    public async Task<ElsaInstanceMigrationWriteResult?> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claim = await store.TryClaimDueAsync(_timeProvider.GetUtcNow(), _leaseDuration, cancellationToken);
        if (claim is null)
            return null;

        var migration = claim.Migration;
        using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var releaseTask = ReleaseAsync(claim, providerCancellation.Token);
        while (!releaseTask.IsCompleted)
        {
            var delay = Task.Delay(_leaseDuration / 2, _timeProvider, cancellationToken);
            if (await Task.WhenAny(releaseTask, delay) == releaseTask)
                break;
            if (!await store.RenewAsync(claim, _timeProvider.GetUtcNow(), _leaseDuration, cancellationToken))
            {
                await providerCancellation.CancelAsync();
                try
                {
                    await releaseTask;
                }
                catch (OperationCanceledException) when (providerCancellation.IsCancellationRequested)
                {
                    // Observe the provider task before surrendering the claim.
                }
                return new(ElsaInstanceMigrationWriteOutcome.Conflict, migration,
                    "migration.source-release.lease-lost");
            }
        }

        var result = await releaseTask;
        return await store.CompleteAsync(claim, result, _timeProvider.GetUtcNow(), cancellationToken);
    }

    private async Task<ElsaInstanceSourceReleaseResult> ReleaseAsync(
        ElsaInstanceMigrationSourceReleaseClaim claim, CancellationToken cancellationToken)
    {
        var migration = claim.Migration;
        try
        {
            return (await releasePort.ReleaseAsync(migration.OrganizationId, migration.WorkspaceId,
                migration.InstanceId, migration.Id, migration.OperationId, claim.AttemptNumber,
                $"migration-source-release:{migration.Id:N}", migration.Source, cancellationToken)).Validate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(ElsaInstanceSourceReleaseOutcome.Ambiguous, "migration.source-release.ambiguous");
        }
    }
}
