using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

public enum ElsaInstanceSourceReleaseOutcome { Confirmed, RetryableFailure, Ambiguous }

public sealed record ElsaInstanceSourceReleaseResult(
    ElsaInstanceSourceReleaseOutcome Outcome, string DiagnosticCode,
    string? ProviderCorrelationId = null, string? EvidenceReference = null, string? EvidenceDigest = null)
{
    public ElsaInstanceSourceReleaseResult Validate()
    {
        if (!Enum.IsDefined(Outcome) || string.IsNullOrWhiteSpace(DiagnosticCode) || DiagnosticCode.Length > 128 ||
            DiagnosticCode.Any(character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-')) ||
            (Outcome == ElsaInstanceSourceReleaseOutcome.Confirmed) !=
            (ProviderCorrelationId is not null && EvidenceReference is not null && EvidenceDigest is not null))
            throw new ArgumentException("Source release result is invalid.");
        if (ProviderCorrelationId is not null)
        {
            if (ProviderCorrelationId.Length > 128 || ProviderCorrelationId.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/')))
                throw new ArgumentException("Provider correlation identifier is invalid.", nameof(ProviderCorrelationId));
            _ = new ElsaInstanceCleanupEvidence(EvidenceReference!, EvidenceDigest!);
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
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceMigrationWriteResult?> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claim = await store.TryClaimDueAsync(_timeProvider.GetUtcNow(), LeaseDuration, cancellationToken);
        if (claim is null)
            return null;

        var migration = claim.Migration;
        using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var releaseTask = ReleaseAsync(claim, providerCancellation.Token);
        while (!releaseTask.IsCompleted)
        {
            var delay = Task.Delay(LeaseDuration / 2, _timeProvider, cancellationToken);
            if (await Task.WhenAny(releaseTask, delay) == releaseTask)
                break;
            if (!await store.RenewAsync(claim, _timeProvider.GetUtcNow(), LeaseDuration, cancellationToken))
            {
                await providerCancellation.CancelAsync();
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
