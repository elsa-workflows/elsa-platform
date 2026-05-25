using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class DeploymentRunServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _sourceRevisionId = Guid.NewGuid();
    private readonly Guid _targetEnvironmentId = Guid.NewGuid();
    private readonly Guid _targetEngineId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.Parse("2026-05-25T10:00:00Z"));
    private readonly RecordingMutationStore _store = new();

    [Fact]
    public async Task Queues_deployment_after_consuming_matching_confirmation()
    {
        var confirmations = new ConfirmationService(_store, _clock);
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, _sourceRevisionId.ToString("D"), _accountId));
        var service = new DeploymentRunService(_store, confirmations, _clock);

        var run = await service.QueueDeploymentAsync(_workspaceId, RunRequest(), confirmation.Id);

        run.Status.Should().Be(WorkspaceDeploymentRunStatus.Queued);
        run.ConfirmationId.Should().Be(confirmation.Id);
        run.ActorAccountId.Should().Be(_accountId);
        _store.Confirmations[confirmation.Id].UsedAt.Should().Be(_clock.GetUtcNow());
        _store.CreatedRuns.Should().ContainSingle();
    }

    [Fact]
    public async Task Queues_rollback_with_source_run_reference()
    {
        var rollbackSourceRunId = Guid.NewGuid();
        var confirmations = new ConfirmationService(_store, _clock);
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Rollback, _sourceRevisionId.ToString("D"), _accountId));
        var service = new DeploymentRunService(_store, confirmations, _clock);

        var run = await service.QueueRollbackAsync(_workspaceId, RunRequest(), confirmation.Id, rollbackSourceRunId);

        run.RollbackSourceRunId.Should().Be(rollbackSourceRunId);
        run.Status.Should().Be(WorkspaceDeploymentRunStatus.Queued);
    }

    [Fact]
    public async Task Rejects_active_run_conflict_without_consuming_confirmation()
    {
        _store.HasActiveRun = true;
        var confirmations = new ConfirmationService(_store, _clock);
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, _sourceRevisionId.ToString("D"), _accountId));
        var service = new DeploymentRunService(_store, confirmations, _clock);

        var act = () => service.QueueDeploymentAsync(_workspaceId, RunRequest(), confirmation.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("An active deployment run already exists for the target environment.");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
    }

    private WorkspaceDeploymentRunRequest RunRequest() =>
        new(_sourceRevisionId, _targetEnvironmentId, _targetEngineId, _accountId, DeploymentRunMode.Apply);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingMutationStore : IWorkspaceDeploymentMutationStore
    {
        public Dictionary<Guid, ActionConfirmation> Confirmations { get; } = [];
        public List<WorkspaceDeploymentRun> CreatedRuns { get; } = [];
        public bool HasActiveRun { get; set; }

        public Task<ActionConfirmation> CreateConfirmationAsync(Guid workspaceId, CreateActionConfirmationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var confirmation = new ActionConfirmation(Guid.NewGuid(), workspaceId, request.ActionType, request.TargetId, request.ConfirmedByAccountId, now, now.AddMinutes(5), null);
            Confirmations[confirmation.Id] = confirmation;
            return Task.FromResult(confirmation);
        }

        public Task<ActionConfirmation?> GetConfirmationAsync(Guid workspaceId, Guid confirmationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Confirmations.GetValueOrDefault(confirmationId));

        public Task<ActionConfirmation> MarkConfirmationUsedAsync(Guid workspaceId, Guid confirmationId, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
        {
            Confirmations[confirmationId] = Confirmations[confirmationId] with { UsedAt = usedAt };
            return Task.FromResult(Confirmations[confirmationId]);
        }

        public Task<bool> HasActiveRunAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasActiveRun);

        public Task<WorkspaceDeploymentRun> CreateRunAsync(Guid workspaceId, QueueWorkspaceDeploymentRunRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var run = new WorkspaceDeploymentRun(
                Guid.NewGuid(),
                workspaceId,
                Guid.NewGuid(),
                request.TargetEnvironmentId,
                request.TargetEngineId,
                request.SourceRevisionId,
                null,
                request.RollbackSourceRunId,
                WorkspaceDeploymentRunStatus.Queued,
                DeploymentValidationOutcome.Passed,
                request.ConfirmationId,
                request.ActorAccountId,
                now,
                null,
                null,
                now,
                null,
                null,
                1,
                null,
                null);
            CreatedRuns.Add(run);
            return Task.FromResult(run);
        }

        public Task<WorkspaceDeploymentRun?> GetRunAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(Guid workspaceId, Guid runId, WorkspaceDeploymentRunStatus status, string message, DateTimeOffset now, string? failureMessage = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
