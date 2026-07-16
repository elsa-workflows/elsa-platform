using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class ConfirmationServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.Parse("2026-05-25T10:00:00Z"));
    private readonly RecordingMutationStore _store = new();

    [Fact]
    public async Task Same_user_can_consume_confirmation_once()
    {
        var service = new ConfirmationService(_store, _clock);
        var confirmation = await service.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, _targetId.ToString("D"), _accountId));

        var first = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, _accountId, ConfirmationActionType.Deploy, _targetId.ToString("D"));
        var replay = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, _accountId, ConfirmationActionType.Deploy, _targetId.ToString("D"));

        first.Succeeded.Should().BeTrue();
        first.Confirmation!.UsedAt.Should().Be(_clock.GetUtcNow());
        replay.Succeeded.Should().BeFalse();
        replay.Validation.Id.Should().Be("deployment.confirmation.used");
    }

    [Fact]
    public async Task Different_user_cannot_consume_confirmation()
    {
        var service = new ConfirmationService(_store, _clock);
        var confirmation = await service.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, _targetId.ToString("D"), _accountId));

        var result = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, Guid.NewGuid(), ConfirmationActionType.Deploy, _targetId.ToString("D"));

        result.Succeeded.Should().BeFalse();
        result.Validation.Id.Should().Be("deployment.confirmation.account");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Expired_confirmation_is_rejected_without_being_used()
    {
        var service = new ConfirmationService(_store, _clock);
        var confirmation = await service.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Rollback, _targetId.ToString("D"), _accountId, TimeSpan.FromSeconds(30)));
        _clock.Advance(TimeSpan.FromSeconds(31));

        var result = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, _accountId, ConfirmationActionType.Rollback, _targetId.ToString("D"));

        result.Succeeded.Should().BeFalse();
        result.Validation.Id.Should().Be("deployment.confirmation.expired");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Mismatched_action_or_target_is_rejected()
    {
        var service = new ConfirmationService(_store, _clock);
        var confirmation = await service.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, _targetId.ToString("D"), _accountId));

        var actionMismatch = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, _accountId, ConfirmationActionType.Rollback, _targetId.ToString("D"));
        var targetMismatch = await service.ConsumeConfirmationAsync(_workspaceId, confirmation.Id, _accountId, ConfirmationActionType.Deploy, Guid.NewGuid().ToString("D"));

        actionMismatch.Validation.Id.Should().Be("deployment.confirmation.target");
        targetMismatch.Validation.Id.Should().Be("deployment.confirmation.target");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class RecordingMutationStore : IWorkspaceDeploymentMutationStore
    {
        public Dictionary<Guid, ActionConfirmation> Confirmations { get; } = [];

        public Task<ActionConfirmation> CreateConfirmationAsync(Guid workspaceId, CreateActionConfirmationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var confirmation = new ActionConfirmation(
                Guid.NewGuid(),
                workspaceId,
                request.ActionType,
                request.TargetId,
                request.ConfirmedByAccountId,
                now,
                now.Add(request.Lifetime ?? TimeSpan.FromMinutes(5)),
                null);
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

        public Task<bool> HasActiveRunAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> CreateRunAsync(Guid workspaceId, QueueWorkspaceDeploymentRunRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> GetRunAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(Guid workspaceId, Guid runId, WorkspaceDeploymentRunStatus status, string message, DateTimeOffset now, string? failureMessage = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(Guid workspaceId, RuntimeControlExecution execution, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
