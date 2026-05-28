namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentQueueWorker(
    IWorkspaceDeploymentMutationStore? store = null,
    IWorkspaceDeploymentCommandStore? commandStore = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DeploymentQueueWorker(IWorkspaceDeploymentMutationStore? store, TimeProvider? timeProvider)
        : this(store, null, timeProvider)
    {
    }

    public async Task<WorkspaceDeploymentRun?> ProcessNextQueuedRunAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        var now = _timeProvider.GetUtcNow();
        var claimed = await store.ClaimNextQueuedRunAsync(workerId, now, cancellationToken);
        if (claimed is null)
            return null;

        if (commandStore is not null)
        {
            var command = (await commandStore.PollPendingCommandsAsync(
                    claimed.WorkspaceId,
                    claimed.EngineId,
                    100,
                    _timeProvider.GetUtcNow(),
                    cancellationToken))
                .FirstOrDefault(x => x.RunId == claimed.Id);
            if (command is not null)
            {
                var leaseToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
                await commandStore.ClaimCommandAsync(
                    claimed.WorkspaceId,
                    command.Id,
                    new ClaimDeploymentCommandRequest(claimed.EngineId, workerId, TimeSpan.FromMinutes(5)),
                    leaseToken,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                await commandStore.CompleteCommandAsync(
                    claimed.WorkspaceId,
                    command.Id,
                    new CompleteDeploymentCommandRequest(leaseToken, null, null, []),
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                return await store.GetRunAsync(claimed.WorkspaceId, claimed.Id, cancellationToken);
            }
        }

        return await store.UpdateRunStatusAsync(
            claimed.WorkspaceId,
            claimed.Id,
            WorkspaceDeploymentRunStatus.Succeeded,
            "Deployment run completed.",
            _timeProvider.GetUtcNow(),
            cancellationToken: cancellationToken);
    }

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
