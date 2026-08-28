namespace ElsaControl.Deployment.Core.Workspace;

public sealed class DeploymentQueueWorker(
    IWorkspaceDeploymentMutationStore? store = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<int> RecoverStaleRunsAsync(TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        return store.MarkStaleRunningRunsRecoveryRequiredAsync(_timeProvider.GetUtcNow(), staleAfter, cancellationToken);
    }

    public WorkspaceDeploymentRunStatus RecoverStatus(WorkspaceDeploymentRun run, DateTimeOffset now, TimeSpan staleAfter)
    {
        if (run.Status == WorkspaceDeploymentRunStatus.Queued)
            return WorkspaceDeploymentRunStatus.Queued;

        if (run.Status != WorkspaceDeploymentRunStatus.Running)
            return run.Status;

        var heartbeat = run.WorkerHeartbeatAt ?? run.StartedAt ?? run.QueuedAt;
        return now - heartbeat > staleAfter
            ? WorkspaceDeploymentRunStatus.RecoveryRequired
            : WorkspaceDeploymentRunStatus.Running;
    }
}
