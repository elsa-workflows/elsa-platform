using ValenceControl.Healing.Core.Verification;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Security;
using FluentAssertions;

namespace ValenceControl.Healing.Core.Tests.Verification;

public sealed class HealingVerificationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task Deployment_alone_is_deployed_unverified_and_cannot_close_the_incident()
    {
        var fixture = Fixture.Create();

        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now));

        fixture.Scope.EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.DeployedUnverified);
        fixture.Scope.Incident.Status.Should().Be(HealingIncidentStatus.Verifying);
        fixture.Store.Scopes.Single().Verification!.RelevantOperationSuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task Positive_operation_must_be_inside_the_deployment_window_and_not_in_the_future()
    {
        var fixture = Fixture.Create();
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-5)));

        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId, fixture.Scope.Episode.Id, fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision, Now.AddMinutes(-6))).Should().BeFalse("it predates deployment");
        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId, fixture.Scope.Episode.Id, fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision, Now.AddMinutes(1))).Should().BeFalse("future success cannot satisfy a gate");
        fixture.Store.Scopes.Single().Verification!.RelevantOperationSuccessCount.Should().Be(0);

        var expired = Fixture.Create(verificationWindow: TimeSpan.FromHours(1));
        await expired.Service.ObserveDeploymentAsync(expired.Observation(Now.AddHours(-2)));
        (await expired.Service.RecordEpisodePositiveOperationAsync(
            expired.Scope.Incident.WorkspaceId, expired.Scope.Episode.Id, expired.Scope.EnvironmentImpact.EnvironmentId,
            expired.Scope.RepairedRevision, Now.AddMinutes(-30))).Should().BeFalse("it is after the verification window");
    }

    [Fact]
    public async Task Replayed_or_out_of_order_positive_operation_is_idempotent()
    {
        var fixture = Fixture.Create();
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-5)));
        var observedAt = Now.AddMinutes(-1);

        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId, fixture.Scope.Episode.Id, fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision, observedAt)).Should().BeTrue();
        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId, fixture.Scope.Episode.Id, fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision, observedAt)).Should().BeTrue();

        fixture.Store.Scopes.Single().Verification!.RelevantOperationSuccessCount.Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-525_601)]
    public async Task Deployment_observations_reject_future_or_unbounded_timestamps(int minutesFromNow)
    {
        var fixture = Fixture.Create();
        var service = new DeploymentObservationService(
            fixture.Store,
            fixture.Service,
            new HealingAuditService(new InMemoryAuditStore(), fixture.Time),
            fixture.Time);
        var request = new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol,
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Incident.ApplicationId,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            Now.AddMinutes(minutesFromNow),
            DeploymentObservationSources.ExternalDelivery,
            "deployment-1",
            "trusted-delivery",
            $"sha256:{new string('a', 64)}",
            "key-1");

        var action = () => service.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Deployment_observation_rejects_non_hex_evidence_digest_before_persistence()
    {
        var fixture = Fixture.Create();
        var service = new DeploymentObservationService(
            fixture.Store,
            fixture.Service,
            new HealingAuditService(new InMemoryAuditStore(), fixture.Time),
            fixture.Time);
        var request = new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol,
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Incident.ApplicationId,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            Now,
            DeploymentObservationSources.ExternalDelivery,
            "deployment-1",
            "trusted-delivery",
            $"sha256:{new string('z', 64)}",
            "key-1");

        var action = () => service.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>();
        fixture.Store.DeploymentAppendCount.Should().Be(0);
    }

    [Fact]
    public async Task Positive_operation_and_elapsed_recurrence_free_window_heal_one_environment()
    {
        var fixture = Fixture.Create(verificationWindow: TimeSpan.FromMinutes(10));
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-11)));

        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId, fixture.Scope.Episode.Id, fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision, Now.AddMinutes(-2))).Should().BeTrue();

        fixture.Scope.EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.Healed);
        fixture.Scope.Incident.Status.Should().Be(HealingIncidentStatus.Healed);
        fixture.Scope.Episode.Outcome.Should().Be(IncidentEpisodeOutcome.Healed);
    }

    [Fact]
    public async Task One_healed_environment_does_not_close_an_episode_with_an_unverified_environment()
    {
        var fixture = Fixture.Create(environmentCount: 2, verificationWindow: TimeSpan.FromMinutes(10));
        foreach (var scope in fixture.Store.Scopes.ToArray())
            await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-11), scope.EnvironmentImpact.EnvironmentId));
        var first = fixture.Store.Scopes[0];

        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            first.Incident.WorkspaceId, first.Episode.Id, first.EnvironmentImpact.EnvironmentId,
            first.RepairedRevision, Now.AddMinutes(-1))).Should().BeTrue();

        first.EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.Healed);
        fixture.Store.Scopes[1].EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.DeployedUnverified);
        first.Incident.Status.Should().Be(HealingIncidentStatus.Verifying);
        first.Episode.Outcome.Should().Be(IncidentEpisodeOutcome.Active);
    }

    [Fact]
    public async Task Matching_recurrence_fails_verification_and_needs_human_attention()
    {
        var fixture = Fixture.Create();
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-1)));
        var occurrence = fixture.Occurrence(Now);
        fixture.Store.OccurrenceScope = fixture.Store.Scopes.Single();

        var failed = await fixture.Service.RecordRecurrenceAsync(occurrence);
        var replay = await fixture.Service.RecordRecurrenceAsync(occurrence);

        failed.Should().NotBeNull();
        replay.Should().NotBeNull();
        fixture.Store.Scopes.Single().Verification!.RecurrenceCount.Should().Be(1);
        fixture.Scope.EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.FailedVerification);
        fixture.Scope.Incident.Status.Should().Be(HealingIncidentStatus.FailedVerification);
        fixture.Scope.Incident.NeedsHumanReason.Should().Be(NeedsHumanReason.VerificationFailed);
    }

    [Fact]
    public async Task Recurrence_state_audit_and_failure_signal_share_one_transaction()
    {
        var fixture = Fixture.Create();
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-1)));
        var occurrence = fixture.Occurrence(Now);
        fixture.Store.OccurrenceScope = fixture.Store.Scopes.Single();
        var auditStore = new RecordingAuditStore();
        var signalSink = new RecordingFailureSignalSink();
        var service = new HealingVerificationService(
            fixture.Store,
            fixture.Time,
            new HealingAuditService(auditStore, fixture.Time),
            signalSink);
        var transactionsBeforeRecurrence = fixture.Store.TransactionCount;

        await service.RecordRecurrenceAsync(occurrence);
        fixture.Time.UtcNow = Now.AddMinutes(1);
        await service.RecordRecurrenceAsync(occurrence);

        fixture.Store.TransactionCount.Should().Be(transactionsBeforeRecurrence + 2);
        auditStore.Events.Should().ContainSingle(x => x.EventType == "verification-failed");
        signalSink.Signals.Should().HaveCount(2).And.OnlyContain(x =>
            x.SupportingOccurrenceId == occurrence.Id && x.DetectedAt == Now);
    }

    [Fact]
    public async Task Later_different_deployment_supersedes_an_open_verification()
    {
        var fixture = Fixture.Create();
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-2)));

        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now, revision: "newer-revision"));
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddMinutes(-2)));

        fixture.Scope.EnvironmentImpact.VerificationStatus.Should().Be(VerificationOutcome.Superseded);
        fixture.Scope.EnvironmentImpact.CurrentDeployedRevision.Should().Be("newer-revision");
        fixture.Scope.Incident.Status.Should().Be(HealingIncidentStatus.Superseded);
    }

    [Fact]
    public async Task Renewed_temporary_waiver_appends_distinct_audit_events_after_expiry()
    {
        var fixture = Fixture.Create();
        var auditStore = new RecordingAuditStore();
        var service = new HealingVerificationService(
            fixture.Store,
            fixture.Time,
            new HealingAuditService(auditStore, fixture.Time));
        await service.ObserveDeploymentAsync(fixture.Observation(Now));

        (await service.WaiveAsync(
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Episode.Id,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            "workspace-owner",
            "first-maintenance-window",
            Now.AddHours(1))).Should().BeTrue();
        fixture.Time.UtcNow = Now.AddHours(2);
        (await service.ExpireWaiverAsync(fixture.Store.Scopes.Single(), fixture.Time.UtcNow)).Should().BeTrue();
        (await service.WaiveAsync(
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Episode.Id,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            "workspace-owner",
            "renewed-maintenance-window",
            fixture.Time.UtcNow.AddHours(1))).Should().BeTrue();

        auditStore.Events.Select(x => x.EventType).Should().Equal(
            "verification-waived",
            "verification-waiver-expired",
            "verification-waived");
        auditStore.Events.Select(x => x.CorrelationId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Expired_waiver_starts_a_fresh_verification_window_for_an_existing_deployment()
    {
        var fixture = Fixture.Create(verificationWindow: TimeSpan.FromMinutes(10));
        await fixture.Service.ObserveDeploymentAsync(fixture.Observation(Now.AddHours(-2)));
        (await fixture.Service.WaiveAsync(
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Episode.Id,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            "workspace-owner",
            "temporary-risk-acceptance",
            Now.AddMinutes(5))).Should().BeTrue();

        fixture.Time.UtcNow = Now.AddMinutes(6);
        var expiredAt = fixture.Time.UtcNow;
        (await fixture.Service.ExpireWaiverAsync(fixture.Store.Scopes.Single(), expiredAt)).Should().BeTrue();

        var verification = fixture.Store.Scopes.Single().Verification!;
        verification.Outcome.Should().Be(VerificationOutcome.DeployedUnverified);
        verification.WindowStartedAt.Should().Be(expiredAt);
        verification.WindowEndsAt.Should().Be(expiredAt.AddMinutes(10));
        verification.RelevantOperationSuccessCount.Should().Be(0);

        fixture.Time.UtcNow = expiredAt.AddMinutes(1);
        (await fixture.Service.RecordEpisodePositiveOperationAsync(
            fixture.Scope.Incident.WorkspaceId,
            fixture.Scope.Episode.Id,
            fixture.Scope.EnvironmentImpact.EnvironmentId,
            fixture.Scope.RepairedRevision,
            fixture.Time.UtcNow)).Should().BeTrue();
        verification.Outcome.Should().Be(VerificationOutcome.DeployedUnverified);

        fixture.Time.UtcNow = expiredAt.AddMinutes(10);
        (await fixture.Service.EvaluateDueAsync(fixture.Store.Scopes.Single(), fixture.Time.UtcNow)).Should().BeTrue();
        verification.Outcome.Should().Be(VerificationOutcome.Healed);
    }

    private sealed record Fixture(
        InMemoryVerificationStore Store,
        MutableTimeProvider Time,
        HealingVerificationService Service,
        HealingVerificationScope Scope)
    {
        public static Fixture Create(int environmentCount = 1, TimeSpan? verificationWindow = null)
        {
            var workspaceId = Guid.NewGuid();
            var applicationId = Guid.NewGuid();
            var incident = new HealingIncident
            {
                Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                Status = HealingIncidentStatus.Merged
            };
            var episode = new IncidentEpisode
            {
                Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                IncidentId = incident.Id, Outcome = IncidentEpisodeOutcome.Active
            };
            incident.ActiveEpisodeId = episode.Id;
            var configuration = new HealingConfiguration
            {
                WorkspaceId = workspaceId, ApplicationId = applicationId,
                VerificationWindow = verificationWindow ?? TimeSpan.FromHours(1)
            };
            var scopes = Enumerable.Range(0, environmentCount).Select(_ =>
            {
                var impact = new EnvironmentImpact
                {
                    Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                    EpisodeId = episode.Id, EnvironmentId = Guid.NewGuid(),
                    VerificationStatus = VerificationOutcome.PendingDeployment
                };
                return new HealingVerificationScope(incident, episode, impact, configuration, "fixed-revision", null);
            }).ToList();
            var store = new InMemoryVerificationStore(scopes);
            var time = new MutableTimeProvider(Now);
            return new Fixture(store, time, new HealingVerificationService(store, time), scopes[0]);
        }

        public DeploymentObservation Observation(DateTimeOffset deployedAt, Guid? environmentId = null, string revision = "fixed-revision") => new()
        {
            Id = Guid.NewGuid(), WorkspaceId = Scope.Incident.WorkspaceId,
            ApplicationId = Scope.Incident.ApplicationId,
            EnvironmentId = environmentId ?? Scope.EnvironmentImpact.EnvironmentId,
            Revision = revision, DeployedAt = deployedAt, AcceptedAt = Now
        };

        public IncidentOccurrence Occurrence(DateTimeOffset occurredAt) => new()
        {
            Id = Guid.NewGuid(), WorkspaceId = Scope.Incident.WorkspaceId,
            ApplicationId = Scope.Incident.ApplicationId, IncidentId = Scope.Incident.Id,
            EpisodeId = Scope.Episode.Id, EnvironmentId = Scope.EnvironmentImpact.EnvironmentId,
            OccurredAt = occurredAt
        };
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class InMemoryVerificationStore(List<HealingVerificationScope> scopes) : IHealingVerificationStore
    {
        public List<HealingVerificationScope> Scopes { get; } = scopes;
        public HealingVerificationScope? OccurrenceScope { get; set; }
        public int DeploymentAppendCount { get; private set; }
        public int TransactionCount { get; private set; }

        public async ValueTask<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default)
        {
            TransactionCount++;
            return await operation(cancellationToken);
        }

        public ValueTask<HealingVerificationAppendResult<DeploymentObservation>> AppendDeploymentObservationAsync(DeploymentObservation observation, CancellationToken cancellationToken = default)
        {
            DeploymentAppendCount++;
            return ValueTask.FromResult(new HealingVerificationAppendResult<DeploymentObservation>(observation, false));
        }

        public ValueTask<VerificationResult> UpsertVerificationAsync(VerificationResult verification, CancellationToken cancellationToken = default)
        {
            var index = Scopes.FindIndex(x => x.Episode.Id == verification.EpisodeId && x.EnvironmentImpact.EnvironmentId == verification.EnvironmentId);
            Scopes[index] = Scopes[index] with { Verification = verification };
            return ValueTask.FromResult(verification);
        }

        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListDeploymentScopesAsync(Guid workspaceId, Guid applicationId, Guid environmentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingVerificationScope>>(Scopes.Where(x => x.EnvironmentImpact.EnvironmentId == environmentId).ToArray());

        public ValueTask<HealingVerificationScope?> GetScopeAsync(Guid workspaceId, Guid episodeId, Guid environmentId, string repairedRevision, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Scopes.SingleOrDefault(x => x.Episode.Id == episodeId && x.EnvironmentImpact.EnvironmentId == environmentId && x.RepairedRevision == repairedRevision));

        public ValueTask<HealingVerificationScope?> GetEpisodeScopeAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Scopes.FirstOrDefault(x => x.Episode.Id == episodeId));

        public ValueTask<HealingVerificationScope?> FindActiveScopeAsync(Guid workspaceId, Guid applicationId, Guid environmentId, string repairedRevision, string operationName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Scopes.SingleOrDefault(x => x.EnvironmentImpact.EnvironmentId == environmentId && x.RepairedRevision == repairedRevision));

        public ValueTask<HealingVerificationScope?> FindScopeForOccurrenceAsync(IncidentOccurrence occurrence, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OccurrenceScope);

        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListDueScopesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingVerificationScope>>(Scopes.Where(x => x.Verification?.WindowEndsAt <= now).Take(take).ToArray());

        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListExpiredWaiverScopesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingVerificationScope>>([]);

        public ValueTask<IReadOnlyList<EnvironmentImpact>> ListEpisodeImpactsAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EnvironmentImpact>>(Scopes.Where(x => x.Episode.Id == episodeId).Select(x => x.EnvironmentImpact).ToArray());

        public ValueTask<IReadOnlyList<VerificationResult>> ListEpisodeVerificationsAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<VerificationResult>>(Scopes.Where(x => x.Episode.Id == episodeId && x.Verification is not null).Select(x => x.Verification!).ToArray());

        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class InMemoryAuditStore : IHealingAuditStore
    {
        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(auditEvent);

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(ValenceControl.Healing.Core.Security.HealingAuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>([]);
    }

    private sealed class RecordingAuditStore : IHealingAuditStore
    {
        public List<HealingAuditEvent> Events { get; } = [];

        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return ValueTask.FromResult(auditEvent);
        }

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(ValenceControl.Healing.Core.Security.HealingAuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>(Events);
    }

    private sealed class RecordingFailureSignalSink : IRepairVerificationSignalSink
    {
        public List<RepairVerificationFailedSignal> Signals { get; } = [];

        public ValueTask AppendAsync(RepairVerificationFailedSignal signal, CancellationToken cancellationToken = default)
        {
            Signals.Add(signal);
            return ValueTask.CompletedTask;
        }
    }
}
