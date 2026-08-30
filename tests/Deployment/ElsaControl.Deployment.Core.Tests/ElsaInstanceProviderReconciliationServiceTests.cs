using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class ElsaInstanceProviderReconciliationServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkspaceId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ElsaInstanceProviderObservationKind.Unknown, ElsaInstanceProviderReconciliationService.UnknownCode)]
    [InlineData(ElsaInstanceProviderObservationKind.Ambiguous, ElsaInstanceProviderReconciliationService.AmbiguousCode)]
    public async Task Uncertain_observation_remains_unknown_and_recovery_required(
        ElsaInstanceProviderObservationKind kind,
        string expectedCode)
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var observation = new ElsaInstanceProviderObservation(
            kind, ElsaObservedLifecycle.Unknown, ElsaInstanceProviderHealthGate.Unknown, "observation-1");

        var result = await Service(store, new RecordingPort(observation)).ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Unknown, result.Instance.ObservedLifecycle);
        Assert.Equal(ElsaInstanceHealth.Unknown, result.Instance.Health);
        Assert.Equal(ElsaInstanceOperationState.RecoveryRequired, result.Operation.State);
        Assert.Equal(expectedCode, result.DiagnosticCode);
        Assert.False(result.RetrySafe);
    }

    [Fact]
    public async Task Retry_safety_is_explicit_but_does_not_trigger_a_blind_retry()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var evidence = new ElsaInstanceProviderRetryEvidence(
            "recovery-proof-1",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var observation = new ElsaInstanceProviderObservation(
            ElsaInstanceProviderObservationKind.Unknown,
            ElsaObservedLifecycle.Unknown,
            ElsaInstanceProviderHealthGate.Unknown,
            "observation-1",
            evidence);

        var result = await Service(store, new RecordingPort(observation)).ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.True(result.RetrySafe);
        Assert.Equal(ElsaInstanceOperationState.RecoveryRequired, result.Operation.State);
        Assert.Equal(ElsaObservedLifecycle.Unknown, result.Instance.ObservedLifecycle);
    }

    [Fact]
    public async Task Confirmed_healthy_running_state_converges_deterministically()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var observation = new ElsaInstanceProviderObservation(
            ElsaInstanceProviderObservationKind.Confirmed,
            ElsaObservedLifecycle.Ready,
            ElsaInstanceProviderHealthGate.Passed,
            "observation-healthy");

        var result = await Service(store, new RecordingPort(observation)).ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.Converged, result.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Ready, result.Instance.ObservedLifecycle);
        Assert.Equal(ElsaInstanceHealth.Healthy, result.Instance.Health);
        Assert.Equal(ElsaInstanceOperationState.Succeeded, result.Operation.State);
        Assert.Equal(ElsaInstanceProviderReconciliationService.ConvergedCode, result.DiagnosticCode);
        Assert.Equal(Now, result.ReconciledAt);
    }

    [Fact]
    public async Task Later_positive_evidence_can_converge_after_an_unknown_observation()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var port = new QueuePort(
            new(ElsaInstanceProviderObservationKind.Unknown, ElsaObservedLifecycle.Unknown,
                ElsaInstanceProviderHealthGate.Unknown, "observation-unknown"),
            new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                ElsaInstanceProviderHealthGate.Passed, "observation-converged"));
        var service = Service(store, port);

        var uncertain = await service.ReconcileAsync(WorkspaceId, accepted.Operation.Id);
        var converged = await service.ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.RecoveryRequired, uncertain.Outcome);
        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.Converged, converged.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Ready, converged.Instance.ObservedLifecycle);
        Assert.Equal(2, port.Calls);
    }

    [Theory]
    [InlineData(ElsaInstanceProviderHealthGate.Failed, ElsaInstanceHealth.Degraded, ElsaInstanceProviderReconciliationService.HealthFailedCode)]
    [InlineData(ElsaInstanceProviderHealthGate.Unknown, ElsaInstanceHealth.Unknown, ElsaInstanceProviderReconciliationService.HealthUnknownCode)]
    public async Task Ready_report_without_a_passing_health_gate_never_projects_ready(
        ElsaInstanceProviderHealthGate healthGate,
        ElsaInstanceHealth expectedHealth,
        string expectedCode)
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var observation = new ElsaInstanceProviderObservation(
            ElsaInstanceProviderObservationKind.Confirmed,
            ElsaObservedLifecycle.Ready,
            healthGate,
            "observation-unhealthy");

        var result = await Service(store, new RecordingPort(observation)).ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.HealthGateFailed, result.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Degraded, result.Instance.ObservedLifecycle);
        Assert.NotEqual(ElsaObservedLifecycle.Ready, result.Instance.ObservedLifecycle);
        Assert.Equal(expectedHealth, result.Instance.Health);
        Assert.Equal(ElsaInstanceOperationState.Failed, result.Operation.State);
        Assert.Equal(expectedCode, result.DiagnosticCode);
    }

    [Fact]
    public async Task Completed_reconciliation_replays_without_another_provider_read()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var port = new RecordingPort(new(
            ElsaInstanceProviderObservationKind.Confirmed,
            ElsaObservedLifecycle.Ready,
            ElsaInstanceProviderHealthGate.Passed,
            "observation-replay"));
        var service = Service(store, port);

        var first = await service.ReconcileAsync(WorkspaceId, accepted.Operation.Id);
        var replay = await service.ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(1, port.Calls);
        Assert.Same(first.Instance, replay.Instance);
        Assert.Same(first.Operation, replay.Operation);
    }

    [Fact]
    public async Task Concurrent_conflicting_evidence_fails_closed()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        var barrier = new Barrier(2);
        var observations = new Queue<ElsaInstanceProviderObservation>(
        [
            new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                ElsaInstanceProviderHealthGate.Passed, "observation-a"),
            new(ElsaInstanceProviderObservationKind.Unknown, ElsaObservedLifecycle.Unknown,
                ElsaInstanceProviderHealthGate.Unknown, "observation-b")
        ]);
        var port = new ConcurrentPort(observations, barrier);
        var service = Service(store, port);

        var results = await Task.WhenAll(
            Task.Run(() => CaptureAsync(() => service.ReconcileAsync(WorkspaceId, accepted.Operation.Id))),
            Task.Run(() => CaptureAsync(() => service.ReconcileAsync(WorkspaceId, accepted.Operation.Id))));

        Assert.Single(results, x => x.Result is not null);
        var conflict = Assert.Single(results, x => x.Error is not null).Error;
        Assert.IsType<ElsaInstanceLifecycleConflictException>(conflict);
        Assert.Contains("evidence conflicts", conflict.Message, StringComparison.Ordinal);
        Assert.Single(store.Operations);
        Assert.Single(store.Instances);
    }

    [Fact]
    public async Task Diagnostics_are_stable_and_do_not_include_provider_values()
    {
        var (store, accepted) = await RecoveryTargetAsync();
        const string providerValue = "subscription-secret-123";
        var observation = new ElsaInstanceProviderObservation(
            ElsaInstanceProviderObservationKind.Ambiguous,
            ElsaObservedLifecycle.Unknown,
            ElsaInstanceProviderHealthGate.Unknown,
            providerValue);

        var result = await Service(store, new RecordingPort(observation)).ReconcileAsync(WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationService.AmbiguousCode, result.DiagnosticCode);
        Assert.DoesNotContain(providerValue, result.DiagnosticCode, StringComparison.Ordinal);
    }

    private static ElsaInstanceProviderReconciliationService Service(
        InMemoryElsaInstanceLifecycleStore store,
        IElsaInstanceProviderReconciliationPort port) =>
        new(store, port, new StaticTimeProvider(Now));

    private static async Task<(InMemoryElsaInstanceLifecycleStore Store, ElsaInstanceLifecycleAcceptance Accepted)> RecoveryTargetAsync()
    {
        var store = new InMemoryElsaInstanceLifecycleStore(new StaticTimeProvider(Now));
        var accepted = await new ElsaInstanceLifecycleService(store, new StaticTimeProvider(Now)).CreateAsync(new(
            OrganizationId,
            WorkspaceId,
            "reconciliation-test",
            "reconciliation-test",
            new(
                new("commercial", "5.0", "5.0.1"),
                new("server-studio"),
                new("managed", "westeurope", "dedicated", "standard-small", "public", "managed")),
            "create-reconciliation-test"));
        store.MarkRecoveryRequired(accepted.Operation.Id);
        return (store, accepted);
    }

    private static async Task<(ElsaInstanceProviderReconciliationResult? Result, Exception? Error)> CaptureAsync(
        Func<Task<ElsaInstanceProviderReconciliationResult>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private sealed class RecordingPort(ElsaInstanceProviderObservation observation) : IElsaInstanceProviderReconciliationPort
    {
        public int Calls { get; private set; }

        public Task<ElsaInstanceProviderObservation> ObserveAsync(
            ElsaInstanceProviderReconciliationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(observation);
        }
    }

    private sealed class QueuePort(params ElsaInstanceProviderObservation[] observations) : IElsaInstanceProviderReconciliationPort
    {
        private readonly Queue<ElsaInstanceProviderObservation> _observations = new(observations);
        public int Calls { get; private set; }

        public Task<ElsaInstanceProviderObservation> ObserveAsync(
            ElsaInstanceProviderReconciliationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_observations.Dequeue());
        }
    }

    private sealed class ConcurrentPort(
        Queue<ElsaInstanceProviderObservation> observations,
        Barrier barrier) : IElsaInstanceProviderReconciliationPort
    {
        private readonly object _gate = new();

        public Task<ElsaInstanceProviderObservation> ObserveAsync(
            ElsaInstanceProviderReconciliationRequest request,
            CancellationToken cancellationToken = default)
        {
            ElsaInstanceProviderObservation observation;
            lock (_gate)
                observation = observations.Dequeue();
            barrier.SignalAndWait(cancellationToken);
            return Task.FromResult(observation);
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
