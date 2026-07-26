using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace ValenceControl.Deployment.Core.Tests;

public sealed class DeploymentQueueWorkerTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.Parse("2026-05-25T10:00:00Z"));
    private readonly RecordingMutationStore _store = new();

    [Fact]
    public async Task Recovers_stale_running_runs_without_replaying_them()
    {
        _store.RecoveredCount = 2;
        var worker = new DeploymentQueueWorker(_store, _clock);

        var recovered = await worker.RecoverStaleRunsAsync(TimeSpan.FromMinutes(10));

        recovered.Should().Be(2);
        _store.LastRecoveryCutoff.Should().Be(TimeSpan.FromMinutes(10));
        _store.ClaimedWorkerId.Should().BeNull();
        _store.UpdatedStatuses.Should().BeEmpty();
    }

    [Fact]
    public void Classifies_stale_claimed_runs_as_recovery_required()
    {
        var worker = new DeploymentQueueWorker();
        var run = Run(WorkspaceDeploymentRunStatus.Running) with { WorkerHeartbeatAt = _clock.GetUtcNow().AddMinutes(-11) };

        var status = worker.RecoverStatus(run, _clock.GetUtcNow(), TimeSpan.FromMinutes(10));

        status.Should().Be(WorkspaceDeploymentRunStatus.RecoveryRequired);
    }

    private WorkspaceDeploymentRun Run(WorkspaceDeploymentRunStatus status) =>
        new(
            _runId,
            _workspaceId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            status,
            DeploymentValidationOutcome.Passed,
            Guid.NewGuid(),
            Guid.NewGuid(),
            _clock.GetUtcNow(),
            status == WorkspaceDeploymentRunStatus.Running ? _clock.GetUtcNow() : null,
            null,
            _clock.GetUtcNow(),
            status == WorkspaceDeploymentRunStatus.Running ? "worker-0" : null,
            status == WorkspaceDeploymentRunStatus.Running ? _clock.GetUtcNow() : null,
            1,
            null,
            null);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingMutationStore : IWorkspaceDeploymentMutationStore
    {
        public WorkspaceDeploymentRun? QueuedRun { get; set; }
        public string? ClaimedWorkerId { get; private set; }
        public int RecoveredCount { get; set; }
        public TimeSpan? LastRecoveryCutoff { get; private set; }
        public List<WorkspaceDeploymentRunStatus> UpdatedStatuses { get; } = [];

        public Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ClaimedWorkerId = workerId;
            var run = QueuedRun;
            QueuedRun = null;
            return Task.FromResult(run is null ? null : run with { Status = WorkspaceDeploymentRunStatus.Running, WorkerId = workerId, StartedAt = now, WorkerHeartbeatAt = now });
        }

        public Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(Guid workspaceId, Guid runId, WorkspaceDeploymentRunStatus status, string message, DateTimeOffset now, string? failureMessage = null, CancellationToken cancellationToken = default)
        {
            UpdatedStatuses.Add(status);
            return Task.FromResult(new WorkspaceDeploymentRun(
                runId,
                workspaceId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                status,
                DeploymentValidationOutcome.Passed,
                Guid.NewGuid(),
                Guid.NewGuid(),
                now,
                now,
                now,
                now,
                "worker-1",
                now,
                1,
                null,
                null));
        }

        public Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken = default)
        {
            LastRecoveryCutoff = staleAfter;
            return Task.FromResult(RecoveredCount);
        }

        public Task<ActionConfirmation> CreateConfirmationAsync(Guid workspaceId, CreateActionConfirmationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActionConfirmation?> GetConfirmationAsync(Guid workspaceId, Guid confirmationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConfirmationUseAttempt?> TryMarkConfirmationUsedAsync(Guid workspaceId, Guid confirmationId, DateTimeOffset usedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveRunAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> CreateRunAsync(Guid workspaceId, QueueWorkspaceDeploymentRunRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> GetRunAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(Guid workspaceId, RuntimeControlExecution execution, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
