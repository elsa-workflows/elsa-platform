using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class ElsaInstanceMigrationServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_persists_exact_references_and_safe_audit()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var result = await service.StartAsync(StartRequest());

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, result.Outcome);
        Assert.Equal("3.10.4", result.Migration!.Source.Version);
        Assert.Equal("4.0.1", result.Migration.Target.Version);
        Assert.Equal("MajorMigrationStarted", store.Audits.Single().EventType);
        Assert.Equal(64, store.Audits.Single().RequestHash.Length);
        Assert.DoesNotContain("request-1", store.Audits.Single().RequestHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_replay_returns_existing_and_conflicting_active_start_fails()
    {
        var store = new RecordingStore();
        var service = Service(store);
        var first = await service.StartAsync(StartRequest());

        var replay = await service.StartAsync(StartRequest());
        var conflict = await service.StartAsync(StartRequest("request-2"));

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(first.Migration!.Id, replay.Migration!.Id);
        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, conflict.Outcome);
    }

    [Fact]
    public async Task Same_start_key_with_changed_target_is_an_idempotency_conflict()
    {
        var store = new RecordingStore();
        var service = Service(store);
        var request = StartRequest();
        await service.StartAsync(request);

        var changed = await service.StartAsync(request with
        {
            Target = Reference("target-2", "5.0", "5.0.0", 'c')
        });

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, changed.Outcome);
        Assert.Single(store.Audits);
    }

    [Fact]
    public async Task Stale_change_is_rejected_without_audit()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new AllowAuthorizer(), clock);
        var started = (await service.StartAsync(StartRequest())).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        var request = Change(started);
        var advanced = await service.AdvanceAsync(request, ElsaInstanceMigrationPhase.Preparing);
        clock.Advance(TimeSpan.FromMinutes(1));

        var stale = await service.AdvanceAsync(request, ElsaInstanceMigrationPhase.ProvisioningTarget);

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, advanced.Outcome);
        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, stale.Outcome);
        Assert.Equal(2, store.Audits.Count);
    }

    [Fact]
    public async Task Same_phase_with_a_different_request_is_a_conflict()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new AllowAuthorizer(), clock);
        var started = (await service.StartAsync(StartRequest())).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        var applied = await service.AdvanceAsync(Change(started), ElsaInstanceMigrationPhase.Preparing);
        clock.Advance(TimeSpan.FromMinutes(1));

        var replay = await service.AdvanceAsync(Change(applied.Migration!), ElsaInstanceMigrationPhase.Preparing);

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Conflict, replay.Outcome);
        Assert.Equal(2, store.Audits.Count);
    }

    [Fact]
    public async Task Exact_change_request_replays_after_the_first_commit()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new AllowAuthorizer(), clock);
        var started = (await service.StartAsync(StartRequest())).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        var request = Change(started);

        var applied = await service.AdvanceAsync(request, ElsaInstanceMigrationPhase.Preparing);
        var replay = await service.AdvanceAsync(request, ElsaInstanceMigrationPhase.Preparing);

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, applied.Outcome);
        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(2, store.Audits.Count);
    }

    [Fact]
    public async Task Cutover_retention_and_authorized_early_release_flow_is_audited()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new AllowAuthorizer(), clock);
        var current = (await service.StartAsync(StartRequest())).Migration!;
        current = await Advance(service, clock, current, ElsaInstanceMigrationPhase.Preparing);
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.CutOverAsync(Change(current), true, ElsaInstanceMigrationSourceAccess.Stopped)).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.RetainSourceAsync(Change(current))).Migration!;
        clock.Advance(TimeSpan.FromHours(1));
        var approver = Guid.NewGuid();
        current = (await service.ApproveEarlyReleaseAsync(Change(current, approver))).Migration!;
        clock.Advance(TimeSpan.FromDays(1));
        var retiring = await service.ReleaseSourceAsync(Change(current));

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, retiring.Outcome);
        Assert.Equal(ElsaInstanceMigrationPhase.RetiringSource, retiring.Migration!.Phase);
        Assert.Equal(approver, retiring.Migration.EarlyReleaseApprovedByAccountId);
        Assert.Contains(store.Audits, x => x.EventType == "MigrationCutover");
        Assert.Contains(store.Audits, x => x.EventType == "MigrationSourceRetained");
        Assert.Contains(store.Audits, x => x.EventType == "MigrationEarlyReleaseApproved");
        Assert.Contains(store.Audits, x => x.EventType == "MigrationSourceReleaseRequested");
    }

    [Fact]
    public async Task Early_release_requires_control_authorization_before_writing()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new DenyEarlyReleaseAuthorizer(), clock);
        var current = (await service.StartAsync(StartRequest())).Migration!;
        current = await Advance(service, clock, current, ElsaInstanceMigrationPhase.Preparing);
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.CutOverAsync(Change(current), true, ElsaInstanceMigrationSourceAccess.ReadOnly)).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.RetainSourceAsync(Change(current))).Migration!;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveEarlyReleaseAsync(Change(current)));

        Assert.DoesNotContain(store.Audits, audit => audit.EventType == "MigrationEarlyReleaseApproved");
    }

    [Fact]
    public async Task Expired_retention_can_request_release_without_early_approval()
    {
        var clock = new MutableTimeProvider(Now);
        var store = new RecordingStore();
        var service = new ElsaInstanceMigrationService(store, new AllowAuthorizer(), clock);
        var current = (await service.StartAsync(StartRequest())).Migration!;
        current = await Advance(service, clock, current, ElsaInstanceMigrationPhase.Preparing);
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.CutOverAsync(Change(current), true, ElsaInstanceMigrationSourceAccess.ReadOnly)).Migration!;
        clock.Advance(TimeSpan.FromMinutes(1));
        current = (await service.RetainSourceAsync(Change(current))).Migration!;
        clock.Advance(TimeSpan.FromDays(31));

        var result = await service.ReleaseSourceAsync(Change(current));

        Assert.Equal(ElsaInstanceMigrationWriteOutcome.Applied, result.Outcome);
        Assert.Equal(ElsaInstanceMigrationPhase.RetiringSource, result.Migration!.Phase);
        Assert.Null(result.Migration.EarlyReleaseApprovedAt);
    }

    private static async Task<ElsaInstanceMigration> Advance(
        ElsaInstanceMigrationService service, MutableTimeProvider clock,
        ElsaInstanceMigration migration, ElsaInstanceMigrationPhase phase)
    {
        clock.Advance(TimeSpan.FromMinutes(1));
        return (await service.AdvanceAsync(Change(migration), phase)).Migration!;
    }

    private static ElsaInstanceMigrationService Service(RecordingStore store) =>
        new(store, new AllowAuthorizer(), new MutableTimeProvider(Now));

    private static ElsaInstanceMigrationStartRequest StartRequest(string requestKey = "request-1") => new(
        OrganizationId, WorkspaceId, InstanceId, 1,
        Reference("source", "3.10", "3.10.4", 'a'),
        Reference("target", "4.0", "4.0.1", 'b'), requestKey, Guid.NewGuid());

    private static ElsaInstanceMigrationChangeRequest Change(ElsaInstanceMigration migration, Guid? actor = null) =>
        new(WorkspaceId, migration.Id, migration.UpdatedAt, Guid.NewGuid().ToString("N"), actor ?? Guid.NewGuid());

    private static ElsaInstanceMigrationReleaseReference Reference(
        string name, string line, string version, char digest) => new(
            $"{name}-plan", $"https://control.example/api/plans/{name}-plan", line, version,
            "sha256:" + new string(digest, 64), $"{name}-deployment");

    private sealed class RecordingStore : IElsaInstanceMigrationStore
    {
        private ElsaInstanceMigration? _migration;
        public List<ElsaInstanceMigrationAudit> Audits { get; } = [];

        public Task<ElsaInstanceMigration?> GetAsync(Guid workspaceId, Guid migrationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_migration is { } value && value.WorkspaceId == workspaceId && value.Id == migrationId ? value : null);

        public Task<ElsaInstanceMigrationWriteResult> CreateAsync(
            ElsaInstanceMigrationStartEnvelope envelope, ElsaInstanceMigrationAudit audit,
            CancellationToken cancellationToken = default)
        {
            var migration = envelope.Migration;
            if (_migration is { } existing)
                return Task.FromResult(existing.StartRequestHash == migration.StartRequestHash
                    ? new ElsaInstanceMigrationWriteResult(ElsaInstanceMigrationWriteOutcome.Replayed, existing, "migration.replayed")
                    : new ElsaInstanceMigrationWriteResult(ElsaInstanceMigrationWriteOutcome.Conflict, existing, "migration.active.conflict"));
            _migration = migration;
            Audits.Add(audit);
            return Task.FromResult(new ElsaInstanceMigrationWriteResult(
                ElsaInstanceMigrationWriteOutcome.Applied, migration, "migration.started"));
        }

        public Task<ElsaInstanceMigrationWriteResult> SaveAsync(
            ElsaInstanceMigration migration, DateTimeOffset expectedUpdatedAt, ElsaInstanceMigrationAudit audit,
            CancellationToken cancellationToken = default)
        {
            if (_migration is null || _migration.UpdatedAt != expectedUpdatedAt)
                return Task.FromResult(new ElsaInstanceMigrationWriteResult(
                    ElsaInstanceMigrationWriteOutcome.Conflict, _migration, "migration.version.conflict"));
            _migration = migration;
            Audits.Add(audit);
            return Task.FromResult(new ElsaInstanceMigrationWriteResult(
                ElsaInstanceMigrationWriteOutcome.Applied, migration, "migration.updated"));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class AllowAuthorizer : IElsaInstanceMigrationAuthorizer
    {
        public Task RequireExecutionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RequireEarlyReleaseAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class DenyEarlyReleaseAuthorizer : IElsaInstanceMigrationAuthorizer
    {
        public Task RequireExecutionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RequireEarlyReleaseAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Denied by test authorization policy.");
    }
}
