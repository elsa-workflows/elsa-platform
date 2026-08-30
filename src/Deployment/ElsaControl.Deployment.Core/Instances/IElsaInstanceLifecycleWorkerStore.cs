namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Provider-neutral persistence port for the asynchronous instance lifecycle
/// boundary. Implementations must claim the Accepted operation with a durable lease,
/// finalize work transactionally, enforce the active environment reservation, and
/// return an existing result on retries. Claiming does not invoke a provider.
/// </summary>
public interface IElsaInstanceLifecycleWorkerStore
{
    Task<ElsaInstanceLifecycleWorkItem?> TryClaimNextAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<ElsaInstanceLifecycleWorkerResult> CommitResolvedAsync(
        ElsaInstanceLifecycleResolutionCommit commit,
        CancellationToken cancellationToken = default);

    Task<ElsaInstanceLifecycleWorkerResult> FailResolutionAsync(
        ElsaInstanceLifecycleResolutionFailure failure,
        CancellationToken cancellationToken = default);
}
