using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class RuntimeControlServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly Guid _engineId = Guid.NewGuid();
    private readonly Guid _environmentId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-05-26T10:00:00Z");
    private readonly RecordingStore _store = new();

    [Fact]
    public void Validates_control_against_matching_capability()
    {
        var service = new RuntimeControlService();
        var control = WorkspaceDeploymentTestFixtures.Control();

        var supported = service.ValidateControl(control, [WorkspaceDeploymentTestFixtures.Capability()]);
        var unsupported = service.ValidateControl(control, [WorkspaceDeploymentTestFixtures.Capability(id: "hosting.restart")]);

        supported.Severity.Should().Be(ValidationSeverity.Pass);
        unsupported.Severity.Should().Be(ValidationSeverity.Blocker);
    }

    [Fact]
    public async Task Executes_supported_control_after_consuming_confirmation()
    {
        _store.Cockpit = Cockpit([WorkspaceDeploymentTestFixtures.Capability()], [WorkspaceDeploymentTestFixtures.Control()]);
        var confirmations = new ConfirmationService(_store, new StaticTimeProvider(_now));
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.RuntimeControl,
                RuntimeControlService.RuntimeControlTargetId(_engineId, "reload-configuration"),
                _accountId));
        var service = new RuntimeControlService(_store, _store, confirmations, new StaticTimeProvider(_now));

        var execution = await service.ExecuteControlAsync(
            _workspaceId,
            new RuntimeControlExecutionRequest(_engineId, "reload-configuration", confirmation.Id, _accountId));

        execution.Status.Should().Be(RuntimeControlExecutionStatus.Succeeded);
        execution.ControlId.Should().Be("reload-configuration");
        _store.Confirmations[confirmation.Id].UsedAt.Should().Be(_now);
        _store.Executions.Should().ContainSingle(x => x.Id == execution.Id);
    }

    [Fact]
    public async Task Rejects_unsupported_control_without_consuming_confirmation()
    {
        _store.Cockpit = Cockpit(
            [WorkspaceDeploymentTestFixtures.Capability(id: "workflow.pause-processing", boundary: CapabilityBoundary.Workflow)],
            [WorkspaceDeploymentTestFixtures.Control()]);
        var confirmations = new ConfirmationService(_store, new StaticTimeProvider(_now));
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.RuntimeControl,
                RuntimeControlService.RuntimeControlTargetId(_engineId, "reload-configuration"),
                _accountId));
        var service = new RuntimeControlService(_store, _store, confirmations, new StaticTimeProvider(_now));

        var act = () => service.ExecuteControlAsync(
            _workspaceId,
            new RuntimeControlExecutionRequest(_engineId, "reload-configuration", confirmation.Id, _accountId));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Runtime control is not supported by the selected engine.");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
        _store.Executions.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_unreachable_engine_without_consuming_confirmation()
    {
        _store.Cockpit = Cockpit(
            [WorkspaceDeploymentTestFixtures.Capability()],
            [WorkspaceDeploymentTestFixtures.Control()],
            DeploymentHealth.Unreachable);
        var confirmations = new ConfirmationService(_store, new StaticTimeProvider(_now));
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.RuntimeControl,
                RuntimeControlService.RuntimeControlTargetId(_engineId, "reload-configuration"),
                _accountId));
        var service = new RuntimeControlService(_store, _store, confirmations, new StaticTimeProvider(_now));

        var act = () => service.ExecuteControlAsync(
            _workspaceId,
            new RuntimeControlExecutionRequest(_engineId, "reload-configuration", confirmation.Id, _accountId));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Runtime control cannot execute while the selected engine is unreachable.");
        _store.Confirmations[confirmation.Id].UsedAt.Should().BeNull();
        _store.Executions.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_missing_or_replayed_confirmation()
    {
        _store.Cockpit = Cockpit([WorkspaceDeploymentTestFixtures.Capability()], [WorkspaceDeploymentTestFixtures.Control()]);
        var confirmations = new ConfirmationService(_store, new StaticTimeProvider(_now));
        var confirmation = await confirmations.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.RuntimeControl,
                RuntimeControlService.RuntimeControlTargetId(_engineId, "reload-configuration"),
                _accountId));
        var service = new RuntimeControlService(_store, _store, confirmations, new StaticTimeProvider(_now));
        await service.ExecuteControlAsync(_workspaceId, new RuntimeControlExecutionRequest(_engineId, "reload-configuration", confirmation.Id, _accountId));

        var replay = () => service.ExecuteControlAsync(
            _workspaceId,
            new RuntimeControlExecutionRequest(_engineId, "reload-configuration", confirmation.Id, _accountId));

        await replay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Confirmation has already been used.");
    }

    private DeploymentCockpit Cockpit(
        IReadOnlyList<EngineCapability> capabilities,
        IReadOnlyList<RuntimeControl> controls,
        DeploymentHealth health = DeploymentHealth.Healthy) =>
        new(
            [],
            [
                new WorkflowEngineRegistration(
                    _engineId.ToString("D"),
                    "claims-prod",
                    _environmentId.ToString("D"),
                    new EngineEndpointMetadata("https://engine.example.test/elsa", "weu", "Elsa 4", CertificateStatus.Trusted),
                    new EngineCredentialReference("Vault", "kv://engine", CredentialVerificationStatus.Verified, _now),
                    health,
                    health == DeploymentHealth.Unreachable ? null : _now,
                    capabilities,
                    controls,
                    null)
            ],
            [],
            [],
            [],
            [],
            []);

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingStore : IWorkspaceDeploymentStore, IWorkspaceDeploymentMutationStore
    {
        public DeploymentCockpit Cockpit { get; set; } = new([], [], [], [], [], [], []);
        public Dictionary<Guid, ActionConfirmation> Confirmations { get; } = [];
        public List<RuntimeControlExecution> Executions { get; } = [];

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Cockpit);

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

        public Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(Guid workspaceId, RuntimeControlExecution execution, CancellationToken cancellationToken = default)
        {
            Executions.Add(execution);
            return Task.FromResult(execution);
        }

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
        public Task<bool> HasActiveRunAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> CreateRunAsync(Guid workspaceId, QueueWorkspaceDeploymentRunRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> GetRunAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(Guid workspaceId, Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(Guid workspaceId, Guid runId, WorkspaceDeploymentRunStatus status, string message, DateTimeOffset now, string? failureMessage = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
