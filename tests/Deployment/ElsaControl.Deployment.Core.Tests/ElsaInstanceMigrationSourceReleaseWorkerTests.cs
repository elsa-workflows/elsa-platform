using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class ElsaInstanceMigrationSourceReleaseWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Confirmed_provider_cleanup_is_required_before_release()
    {
        var migration = RetiringMigration();
        var store = new Store(migration);
        var port = new Port(new(ElsaInstanceSourceReleaseOutcome.Confirmed, "migration.source-release.confirmed",
            "provider-operation-1", "https://evidence.example/migrations/release-1", "sha256:" + new string('d', 64)));
        var worker = new ElsaInstanceMigrationSourceReleaseWorker(store, port, new Clock(Now));

        var result = await worker.RunOnceAsync();

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, result!.Outcome);
        Assert.Equal(ElsaInstanceMigrationPhase.Released, result.Migration!.Phase);
        Assert.Equal(migration.Source, port.Source);
    }

    [Fact]
    public async Task Ambiguous_provider_result_keeps_source_in_retirement()
    {
        var migration = RetiringMigration();
        var store = new Store(migration);
        var worker = new ElsaInstanceMigrationSourceReleaseWorker(store,
            new Port(new(ElsaInstanceSourceReleaseOutcome.Ambiguous, "migration.source-release.ambiguous")), new Clock(Now));

        var result = await worker.RunOnceAsync();

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, result!.Outcome);
        Assert.Equal(ElsaInstanceMigrationPhase.RetiringSource, result.Migration!.Phase);
    }

    [Fact]
    public async Task Lost_lease_cancels_and_observes_the_provider_call()
    {
        var store = new Store(RetiringMigration()) { RenewalAllowed = false };
        var port = new CancellingPort();
        var worker = new ElsaInstanceMigrationSourceReleaseWorker(
            store, port, TimeProvider.System, TimeSpan.FromMilliseconds(20));

        var result = await worker.RunOnceAsync();

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, result!.Outcome);
        Assert.True(port.CancellationObserved);
    }

    [Fact]
    public void Partial_or_nonconfirmed_release_evidence_is_rejected_before_persistence()
    {
        Assert.Throws<ArgumentException>(() => new ElsaInstanceSourceReleaseResult(
            ElsaInstanceSourceReleaseOutcome.Ambiguous, "migration.source-release.ambiguous",
            EvidenceReference: "https://evidence.example/migrations/release-1").Validate());
        Assert.Throws<ArgumentException>(() => new ElsaInstanceSourceReleaseResult(
            ElsaInstanceSourceReleaseOutcome.RetryableFailure, "migration.source-release.retryable",
            "provider-operation-1", "https://evidence.example/migrations/release-1",
            "sha256:" + new string('d', 64)).Validate());
    }

    private static ElsaInstanceMigration RetiringMigration()
    {
        var source = Reference("source", "3.10", "3.10.4", 'a');
        var target = Reference("target", "5.0", "5.0.0", 'b');
        var migration = ElsaInstanceMigration.Plan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), source, target, new string('c', 64), Now.AddDays(-31));
        migration = migration.Advance(ElsaInstanceMigrationPhase.Preparing, Now.AddDays(-31).AddMinutes(1));
        migration = migration.CutOver(true, ElsaInstanceMigrationSourceAccess.Stopped, Now.AddDays(-31).AddMinutes(2));
        migration = migration.RetainSource(Now.AddDays(-31).AddMinutes(3));
        return migration.BeginSourceRetirement(Now.AddMinutes(-1));
    }

    private static ElsaInstanceMigrationReleaseReference Reference(string name, string line, string version, char digest) =>
        new($"{name}-plan", $"https://control.example/api/plans/{name}-plan", line, version,
            "sha256:" + new string(digest, 64), $"{name}-deployment");

    private sealed class Store(ElsaInstanceMigration migration) : IElsaInstanceMigrationSourceReleaseStore
    {
        public bool RenewalAllowed { get; init; } = true;
        public Task<ElsaInstanceMigrationSourceReleaseClaim?> TryClaimDueAsync(DateTimeOffset now, TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ElsaInstanceMigrationSourceReleaseClaim?>(new(migration, Guid.NewGuid(), 1, now.Add(leaseDuration)));

        public Task<ElsaInstanceMigrationWriteResult> CompleteAsync(ElsaInstanceMigrationSourceReleaseClaim claim,
            ElsaInstanceSourceReleaseResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var current = result.Outcome == ElsaInstanceSourceReleaseOutcome.Confirmed
                ? claim.Migration.ConfirmSourceReleased(now)
                : claim.Migration;
            return Task.FromResult(new ElsaInstanceMigrationWriteResult(
                result.Outcome == ElsaInstanceSourceReleaseOutcome.Confirmed
                    ? ElsaInstanceMigrationWriteOutcome.Applied : ElsaInstanceMigrationWriteOutcome.Conflict,
                current, result.DiagnosticCode));
        }

        public Task<bool> RenewAsync(ElsaInstanceMigrationSourceReleaseClaim claim, DateTimeOffset now,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult(RenewalAllowed);
    }

    private sealed class Port(ElsaInstanceSourceReleaseResult result) : IElsaInstanceMigrationSourceReleasePort
    {
        public ElsaInstanceMigrationReleaseReference? Source { get; private set; }
        public Task<ElsaInstanceSourceReleaseResult> ReleaseAsync(Guid organizationId, Guid workspaceId, Guid instanceId,
            Guid migrationId, Guid operationId, int attemptNumber, string idempotencyKey,
            ElsaInstanceMigrationReleaseReference source, CancellationToken cancellationToken = default)
        {
            Source = source;
            return Task.FromResult(result);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CancellingPort : IElsaInstanceMigrationSourceReleasePort
    {
        public bool CancellationObserved { get; private set; }

        public async Task<ElsaInstanceSourceReleaseResult> ReleaseAsync(Guid organizationId, Guid workspaceId,
            Guid instanceId, Guid migrationId, Guid operationId, int attemptNumber, string idempotencyKey,
            ElsaInstanceMigrationReleaseReference source, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The provider call should have been cancelled.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
