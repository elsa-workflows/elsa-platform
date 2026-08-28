using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

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

        Assert.Equal(WorkspaceDeploymentRunStatus.Queued, run.Status);
        Assert.Equal(confirmation.Id, run.ConfirmationId);
        Assert.Equal(_accountId, run.ActorAccountId);
        Assert.Equal(_clock.GetUtcNow(), _store.Confirmations[confirmation.Id].UsedAt);
        Assert.Single(_store.CreatedRuns);
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

        Assert.Equal(rollbackSourceRunId, run.RollbackSourceRunId);
        Assert.Equal(WorkspaceDeploymentRunStatus.Queued, run.Status);
    }

    [Fact]
    public async Task Rejects_rollback_when_target_tier_does_not_allow_rollback()
    {
        _store.Environments.Add(Environment(_targetEnvironmentId, "UAT", EnvironmentTier.Stage, DeploymentTierCapabilities.PromotionTarget));
        var rollbackSourceRunId = Guid.NewGuid();
        var confirmations = new ConfirmationService(_store, _clock);
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Rollback, _sourceRevisionId.ToString("D"), _accountId));
        var service = new DeploymentRunService(_store, confirmations, _clock);

        var act = () => service.QueueRollbackAsync(_workspaceId, RunRequest(), confirmation.Id, rollbackSourceRunId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("UAT does not allow rollback actions.", exception.Message);
        Assert.Null(_store.Confirmations[confirmation.Id].UsedAt);
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("An active deployment run already exists for the target environment.", exception.Message);
        Assert.Null(_store.Confirmations[confirmation.Id].UsedAt);
    }

    private WorkspaceDeploymentRunRequest RunRequest() =>
        new(_sourceRevisionId, _targetEnvironmentId, _targetEngineId, _accountId, DeploymentRunMode.Apply);

    private EnvironmentSummary Environment(Guid environmentId, string name, EnvironmentTier tier, params string[] capabilities) =>
        new(
            environmentId.ToString("D"),
            name,
            tier,
            DeploymentHealth.Healthy,
            new DesiredStateRevision(_sourceRevisionId.ToString("D"), 1, "abc123", "Revision 1", DateTimeOffset.UtcNow),
            null,
            DeploymentStatus.Succeeded,
            DriftStatus.InSync,
            [],
            name,
            DeploymentTierStatus.Active.ToString(),
            capabilities);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingMutationStore : IWorkspaceDeploymentMutationStore, IWorkspaceDeploymentStore
    {
        public Dictionary<Guid, ActionConfirmation> Confirmations { get; } = [];
        public List<WorkspaceDeploymentRun> CreatedRuns { get; } = [];
        public List<EnvironmentSummary> Environments { get; } = [];
        public bool HasActiveRun { get; set; }

        public Task<ActionConfirmation> CreateConfirmationAsync(Guid workspaceId, CreateActionConfirmationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var confirmation = new ActionConfirmation(Guid.NewGuid(), workspaceId, request.ActionType, request.TargetId, request.ConfirmedByAccountId, now, now.AddMinutes(5), null);
            Confirmations[confirmation.Id] = confirmation;
            return Task.FromResult(confirmation);
        }

        public Task<ActionConfirmation?> GetConfirmationAsync(Guid workspaceId, Guid confirmationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Confirmations.GetValueOrDefault(confirmationId));

        public Task<ConfirmationUseAttempt?> TryMarkConfirmationUsedAsync(Guid workspaceId, Guid confirmationId, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
        {
            if (Confirmations[confirmationId].UsedAt is not null)
                return Task.FromResult<ConfirmationUseAttempt?>(new ConfirmationUseAttempt(Confirmations[confirmationId], false));

            Confirmations[confirmationId] = Confirmations[confirmationId] with { UsedAt = usedAt };
            return Task.FromResult<ConfirmationUseAttempt?>(new ConfirmationUseAttempt(Confirmations[confirmationId], true));
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
        public Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(Guid workspaceId, RuntimeControlExecution execution, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeploymentCockpit(
                [new WorkflowApplication(Guid.NewGuid().ToString("D"), "Payments", "Workspace", Environments)],
                [],
                [],
                [],
                [],
                [],
                []));

        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
