namespace Elsa.Platform.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentMutationStore
{
    Task<ActionConfirmation> CreateConfirmationAsync(
        Guid workspaceId,
        CreateActionConfirmationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<ActionConfirmation?> GetConfirmationAsync(
        Guid workspaceId,
        Guid confirmationId,
        CancellationToken cancellationToken = default);

    Task<ActionConfirmation> MarkConfirmationUsedAsync(
        Guid workspaceId,
        Guid confirmationId,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveRunAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentRun> CreateRunAsync(
        Guid workspaceId,
        QueueWorkspaceDeploymentRunRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentRun?> GetRunAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentRunCommandSummary>> GetRunCommandSummariesAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeploymentRunCommandSummary>>([]);

    Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(
        Guid workspaceId,
        Guid runId,
        WorkspaceDeploymentRunStatus status,
        string message,
        DateTimeOffset now,
        string? failureMessage = null,
        CancellationToken cancellationToken = default);

    Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default);

    Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(
        Guid workspaceId,
        RuntimeControlExecution execution,
        CancellationToken cancellationToken = default);
}
