namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationWorkerTests
{
    [Fact]
    public async Task Unrestorable_plan_is_marked_terminal_instead_of_being_polled_forever()
    {
        var operation = Operation();
        var store = new WorkerStore(operation);
        var executor = new AzureProviderExecutor(store, new NeverCalledRunner());
        var worker = new AzureProviderOperationWorker(store, executor, new PersistedAzureProviderPlanSource(), new FixedTimeProvider());

        var processed = await worker.ProcessOnceAsync();

        Assert.Equal(0, processed);
        Assert.Equal(1, store.MarkUnrestorableCount);
        Assert.Equal(AzureProviderOperationStatus.Failed, store.Operation.Status);
        Assert.Contains(store.Transitions, transition => transition.Code == "azure.plan.unrestorable");
    }

    private static AzureProviderOperation Operation() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "workload-a",
        AzureProviderOperationAction.Reconcile,
        "request-1",
        new('a', 64),
        new('b', 64),
        new('c', 64),
        new('d', 64),
        "3.8.0",
        "3.8",
        "combined",
        "Dedicated",
        "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined",
        "sha256:" + new string('e', 64),
        null,
        null,
        AzureProviderOperationStatus.Accepted,
        AzureProviderOperationPhase.Planned,
        0,
        0,
        1,
        new(),
        null,
        AzureProviderHealth.Unknown,
        [],
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        new Dictionary<string, string>());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }

    private sealed class NeverCalledRunner : IAzureProviderRunner
    {
        public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("The provider runner must not be called for an unrestorable plan.");
    }

    private sealed class WorkerStore(AzureProviderOperation operation) : IAzureProviderOperationStore
    {
        public AzureProviderOperation Operation { get; private set; } = operation;
        public List<AzureProviderOperationTransition> Transitions { get; } = [];
        public int MarkUnrestorableCount { get; private set; }

        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(Operation);

        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(Operation);

        public Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AzureProviderOperation>>(Operation.Status is AzureProviderOperationStatus.Accepted or AzureProviderOperationStatus.RecoveryRequired ? [Operation] : []);

        public Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> MarkUnrestorableAsync(Guid workspaceId, Guid operationId, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            MarkUnrestorableCount++;
            if (Operation.Status is not (AzureProviderOperationStatus.Accepted or AzureProviderOperationStatus.Queued or AzureProviderOperationStatus.RecoveryRequired) || expectedVersion.HasValue && expectedVersion.Value != Operation.Version)
                return Task.FromResult<AzureProviderOperation?>(null);

            Operation = Operation with { Status = AzureProviderOperationStatus.Failed, CompletedAt = now, UpdatedAt = now, Version = Operation.Version + 1 };
            Transitions.Add(new(Operation.Id, Operation.Id, Transitions.Count + 1, Operation.Status, Operation.Phase, "azure.plan.unrestorable", "azure.plan.unrestorable", now));
            return Task.FromResult<AzureProviderOperation?>(Operation);
        }

        public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, string message, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AzureProviderOperationTransition>>(Transitions);
    }
}
