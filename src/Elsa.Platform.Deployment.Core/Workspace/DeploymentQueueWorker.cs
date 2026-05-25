namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentQueueWorker
{
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
