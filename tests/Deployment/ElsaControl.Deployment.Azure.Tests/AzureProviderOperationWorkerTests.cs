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

    [Fact]
    public async Task Embedded_only_evidence_digest_is_marked_terminal_instead_of_reaching_execution()
    {
        var digest = "sha256:" + new string('f', 64);
        var operation = WithComputedMetadata(Operation() with
        {
            ReleaseManifestReference = $"oci://evidence.example/manifest@{digest}",
            ReleaseManifestSignatureReference = $"oci://evidence.example/signature@{digest}"
        });
        var store = new WorkerStore(operation);
        var executor = new AzureProviderExecutor(store, new NeverCalledRunner());
        var worker = new AzureProviderOperationWorker(store, executor, new PersistedAzureProviderPlanSource(), new FixedTimeProvider());

        var processed = await worker.ProcessOnceAsync();

        Assert.Equal(0, processed);
        Assert.Equal(1, store.MarkUnrestorableCount);
        Assert.Equal(AzureProviderOperationStatus.Failed, store.Operation.Status);
    }

    [Fact]
    public async Task Persisted_request_hash_mismatch_is_marked_terminal_before_execution()
    {
        var digest = "sha256:" + new string('f', 64);
        var operation = WithComputedMetadata(Operation() with
        {
            ReleaseManifestDigest = digest,
            ReleaseManifestSignatureDigest = digest,
            ReleaseManifestReference = "oci://evidence.example/manifest",
            ReleaseManifestSignatureReference = "oci://evidence.example/signature"
        });
        operation = operation with { RequestHash = new string('x', 64) };
        var store = new WorkerStore(operation);
        var executor = new AzureProviderExecutor(store, new NeverCalledRunner());
        var worker = new AzureProviderOperationWorker(store, executor, new PersistedAzureProviderPlanSource(), new FixedTimeProvider());

        var processed = await worker.ProcessOnceAsync();

        Assert.Equal(0, processed);
        Assert.Equal(1, store.MarkUnrestorableCount);
        Assert.Equal(AzureProviderOperationStatus.Failed, store.Operation.Status);
    }

    [Fact]
    public async Task A_malformed_operation_does_not_starve_later_runnable_operations()
    {
        var digest = "sha256:" + new string('f', 64);
        var malformed = WithComputedMetadata(Operation() with
        {
            TargetKey = "malformed",
            ReleaseManifestReference = $"oci://evidence.example/manifest@{digest}",
            ReleaseManifestSignatureReference = $"oci://evidence.example/signature@{digest}"
        });
        var later = WithComputedMetadata(Operation() with
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetKey = "later",
            ReleaseManifestDigest = digest,
            ReleaseManifestSignatureDigest = digest,
            ReleaseManifestReference = "oci://evidence.example/manifest",
            ReleaseManifestSignatureReference = "oci://evidence.example/signature"
        });
        var store = new MultiOperationWorkerStore(malformed, later);
        var runner = new CompletingRunner();
        var executor = new AzureProviderExecutor(store, runner);
        var worker = new AzureProviderOperationWorker(store, executor, new PersistedAzureProviderPlanSource(), new FixedTimeProvider());

        var processed = await worker.ProcessOnceAsync();

        Assert.Equal(1, processed);
        Assert.Equal(1, store.MarkUnrestorableCount);
        Assert.Equal(AzureProviderOperationStatus.Failed, store.Operations[0].Status);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, store.Operations[1].Status);
        Assert.Equal(1, store.Operations[1].AttemptNumber);
        Assert.Equal(7, runner.Steps.Count);
    }

    [Fact]
    public async Task Asynchronous_executor_failure_after_claim_is_not_misclassified_as_unrestorable()
    {
        var digest = "sha256:" + new string('f', 64);
        var operation = WithComputedMetadata(Operation() with
        {
            ReleaseManifestDigest = digest,
            ReleaseManifestSignatureDigest = digest,
            ReleaseManifestReference = "oci://evidence.example/manifest",
            ReleaseManifestSignatureReference = "oci://evidence.example/signature"
        });
        var store = new WorkerStore(operation, failAfterClaim: true);
        var executor = new AzureProviderExecutor(store, new NeverCalledRunner());
        var worker = new AzureProviderOperationWorker(store, executor, new PersistedAzureProviderPlanSource(), new FixedTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessOnceAsync());

        Assert.Equal(0, store.MarkUnrestorableCount);
        Assert.Equal(AzureProviderOperationStatus.Running, store.Operation.Status);
    }

    private static AzureProviderOperation Operation()
    {
        var operation = new AzureProviderOperation(
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
        return WithComputedMetadata(operation);
    }

    private static AzureProviderOperation WithComputedMetadata(AzureProviderOperation operation)
    {
        var request = new AzureProviderOperationRequest(
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
        return operation with
        {
            RequestHash = AzureProviderOperationValidation.ComputeRequestHash(request),
            OperationIdentity = AzureProviderOperationValidation.ComputeOperationIdentity(request)
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }

    private sealed class NeverCalledRunner : IAzureProviderRunner
    {
        public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("The provider runner must not be called for an unrestorable plan.");
    }

    private sealed class CompletingRunner : IAzureProviderRunner
    {
        public List<AzureProviderRunnerStep> Steps { get; } = [];

        public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken = default)
        {
            Steps.Add(command.Step);
            var phase = command.Step switch
            {
                AzureProviderRunnerStep.Foundation => AzureProviderOperationPhase.FoundationSubmitted,
                AzureProviderRunnerStep.AcrPull or AzureProviderRunnerStep.SeedSecrets => AzureProviderOperationPhase.FoundationSubmitted,
                AzureProviderRunnerStep.SqlBootstrap => AzureProviderOperationPhase.FoundationReady,
                AzureProviderRunnerStep.Workload => AzureProviderOperationPhase.WorkloadReady,
                AzureProviderRunnerStep.Health => AzureProviderOperationPhase.HealthVerified,
                AzureProviderRunnerStep.Promotion => AzureProviderOperationPhase.TrafficPromoted,
                _ => AzureProviderOperationPhase.CleanupVerified
            };
            var hasEndpoint = command.Step is AzureProviderRunnerStep.Health or AzureProviderRunnerStep.Promotion;
            return Task.FromResult(new AzureProviderRunnerResult(
                AzureProviderRunnerOutcome.Completed,
                phase,
                new(),
                hasEndpoint ? AzureProviderHealth.Healthy : AzureProviderHealth.Unknown,
                hasEndpoint ? "https://workload.example.test" : null,
                [],
                "azure.step.completed",
                "Completed."));
        }
    }

    private sealed class WorkerStore(AzureProviderOperation operation, bool failAfterClaim = false) : IAzureProviderOperationStore
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

        public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            if (!failAfterClaim)
                return Task.FromResult<AzureProviderOperation?>(null);

            Operation = Operation with { Status = AzureProviderOperationStatus.Running, Version = Operation.Version + 1 };
            return Task.FromException<AzureProviderOperation?>(new InvalidOperationException("The persisted claim failed after ownership was recorded."));
        }

        public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AzureProviderOperationTransition>>(Transitions);
    }

    private sealed class MultiOperationWorkerStore(params AzureProviderOperation[] operations) : IAzureProviderOperationStore
    {
        public List<AzureProviderOperation> Operations { get; } = operations.ToList();
        public int MarkUnrestorableCount { get; private set; }

        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(Operations.Single(operation => operation.TargetKey == request.TargetKey));

        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Operations.SingleOrDefault(operation => operation.WorkspaceId == workspaceId && operation.Id == operationId));

        public Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AzureProviderOperation>>(Operations.Where(operation => operation.Status is AzureProviderOperationStatus.Accepted or AzureProviderOperationStatus.RecoveryRequired).ToList());

        public Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(null);

        public Task<AzureProviderOperation?> MarkUnrestorableAsync(Guid workspaceId, Guid operationId, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            MarkUnrestorableCount++;
            var index = Operations.FindIndex(operation => operation.WorkspaceId == workspaceId && operation.Id == operationId &&
                operation.Status is AzureProviderOperationStatus.Accepted or AzureProviderOperationStatus.Queued or AzureProviderOperationStatus.RecoveryRequired &&
                (!expectedVersion.HasValue || operation.Version == expectedVersion.Value));
            if (index < 0)
                return Task.FromResult<AzureProviderOperation?>(null);

            var updated = Operations[index] with
            {
                Status = AzureProviderOperationStatus.Failed,
                CompletedAt = now,
                UpdatedAt = now,
                Version = Operations[index].Version + 1
            };
            Operations[index] = updated;
            return Task.FromResult<AzureProviderOperation?>(updated);
        }

        public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            ClaimCoreAsync(workspaceId, operationId, workerId, leaseToken, leaseDuration, now, expectedVersion, allowRecovery: false);

        public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            ClaimCoreAsync(workspaceId, operationId, workerId, leaseToken, leaseDuration, now, expectedVersion, allowRecovery: true);

        public Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperation?>(Operations.SingleOrDefault(operation => operation.Id == operationId));

        public Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            var index = Operations.FindIndex(operation => operation.Id == operationId && operation.Status == AzureProviderOperationStatus.Running &&
                (!expectedVersion.HasValue || operation.Version == expectedVersion.Value));
            if (index < 0)
                return Task.FromResult<AzureProviderOperation?>(null);

            var updated = Operations[index] with
            {
                Phase = checkpoint.Phase,
                Resources = checkpoint.Resources,
                Endpoint = checkpoint.Endpoint,
                Health = checkpoint.Health,
                Diagnostics = checkpoint.Diagnostics,
                CheckpointSequence = Operations[index].CheckpointSequence + 1,
                UpdatedAt = now,
                Version = Operations[index].Version + 1
            };
            Operations[index] = updated;
            return Task.FromResult<AzureProviderOperation?>(updated);
        }

        public Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            var index = Operations.FindIndex(operation => operation.Id == operationId && operation.Status == AzureProviderOperationStatus.Running &&
                (!expectedVersion.HasValue || operation.Version == expectedVersion.Value));
            if (index < 0)
                return Task.FromResult<AzureProviderOperation?>(null);

            var updated = Operations[index] with
            {
                Status = status,
                CompletedAt = status == AzureProviderOperationStatus.RecoveryRequired ? null : now,
                UpdatedAt = now,
                Version = Operations[index].Version + 1,
                WorkerId = null,
                LeaseExpiresAt = null
            };
            Operations[index] = updated;
            return Task.FromResult<AzureProviderOperation?>(updated);
        }

        public Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AzureProviderOperationTransition>>([]);

        private Task<AzureProviderOperation?> ClaimCoreAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion, bool allowRecovery)
        {
            var index = Operations.FindIndex(operation => operation.WorkspaceId == workspaceId && operation.Id == operationId &&
                (operation.Status == AzureProviderOperationStatus.Accepted || operation.Status == AzureProviderOperationStatus.Queued ||
                 allowRecovery && operation.Status == AzureProviderOperationStatus.RecoveryRequired) &&
                (!expectedVersion.HasValue || operation.Version == expectedVersion.Value));
            if (index < 0)
                return Task.FromResult<AzureProviderOperation?>(null);

            var updated = Operations[index] with
            {
                Status = AzureProviderOperationStatus.Running,
                AttemptNumber = Operations[index].AttemptNumber + 1,
                Version = Operations[index].Version + 1,
                WorkerId = workerId,
                LeaseExpiresAt = now.Add(leaseDuration),
                HeartbeatAt = now,
                UpdatedAt = now
            };
            Operations[index] = updated;
            return Task.FromResult<AzureProviderOperation?>(updated);
        }
    }
}
