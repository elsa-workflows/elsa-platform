namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Drains accepted and recoverable Azure operations. The operation store is the queue: claiming
/// remains the executor's compare-and-set boundary, so multiple hosted workers can safely poll
/// the same database without introducing a second, non-durable queue.
/// </summary>
public sealed class AzureProviderOperationWorker(
    IAzureProviderOperationStore store,
    AzureProviderExecutor executor,
    IAzureProviderPlanSource planSource,
    TimeProvider? timeProvider = null)
{
    // Keep the executor-only construction seam available to hosts that predate the persisted
    // plan source; the default source still enforces the same admission checks.
    public AzureProviderOperationWorker(
        IAzureProviderOperationStore store,
        AzureProviderExecutor executor,
        TimeProvider timeProvider)
        : this(store, executor, new PersistedAzureProviderPlanSource(), timeProvider)
    {
    }

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> ProcessOnceAsync(
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var now = _timeProvider.GetUtcNow();
        await store.RecoverStaleAsync(now, cancellationToken);
        var operations = await store.ListRunnableAsync(now, batchSize, cancellationToken);
        var processed = 0;
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = planSource.Resolve(operation);
            if (plan is null)
            {
                // A malformed or legacy persisted plan must not remain runnable forever. The
                // store performs a versioned compare-and-set and records only stable, value-free
                // diagnostics; terminal Failed is deliberately excluded from the poll query.
                await store.MarkUnrestorableAsync(
                    operation.WorkspaceId,
                    operation.Id,
                    now,
                    operation.Version,
                    cancellationToken);
                continue;
            }

            var request = new AzureProviderExecutionRequest(
                CreateRequest(operation),
                plan);
            await executor.ExecuteAsync(request, cancellationToken);
            processed++;
        }

        return processed;
    }

    private static AzureProviderOperationRequest CreateRequest(AzureProviderOperation operation) =>
        new(
            operation.WorkspaceId,
            operation.TargetKey,
            operation.Action,
            operation.IdempotencyKey,
            operation.PlanFingerprint,
            operation.TemplateFingerprint,
            operation.ElsaVersion,
            operation.ReleaseLine,
            operation.Topology,
            operation.Isolation,
            operation.Location,
            operation.ImageRepository,
            operation.ImageDigest,
            operation.ReleaseManifestDigest,
            operation.ReleaseManifestSignatureDigest,
            operation.ReleaseManifestReference,
            operation.ReleaseManifestSignatureReference,
            operation.SafeSecretReferences);
}
