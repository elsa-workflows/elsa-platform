using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Operations;
using ValenceControl.Healing.Core.Providers;

namespace ValenceControl.Healing.Core.Tests.Providers;

public sealed class ProviderOperationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T15:00:00Z");

    [Fact]
    public async Task EnqueueCanonicalizesAndHashesTheDurablePayload()
    {
        var store = new RecordingStore();
        var service = CreateService(store, []);

        var result = await service.EnqueueAsync(new ProviderOperationEnqueueRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ProviderOperationKind.DispatchWorkflow,
            "dispatch-1", "{ \"attempt\": 1 }"));

        Assert.False(result.IsReplay);
        Assert.Equal("{\"attempt\":1}", result.Operation.PayloadJson);
        Assert.Matches("^[0-9a-f]{64}$", result.Operation.PayloadHash);
        Assert.Equal(ProviderOperationStatus.Pending, result.Operation.Status);
        Assert.Equal(Now, result.Operation.CreatedAt);
    }

    [Fact]
    public async Task RunOnceRoutesTheLeasedOperationAndPersistsRetryOutcome()
    {
        var store = new RecordingStore
        {
            Lease = new HealingOperationLease<ProviderOperation>(
                Guid.NewGuid(), "lease-1", NewOperation(ProviderOperationKind.DispatchWorkflow), 1, 2)
        };
        var service = CreateService(store, [new RetryHandler()]);

        var result = await service.RunOnceAsync();

        Assert.Equal(HealingWorkerRunStatus.RetryScheduled, result.Status);
        Assert.Equal(HealingOperationOutcome.Retry("github-rate-limited"), store.Outcome);
        Assert.Equal(Now.AddMinutes(1), store.NextAttemptAt);
    }

    [Fact]
    public async Task MissingTrustedHandlerIsDeadLettered()
    {
        var store = new RecordingStore
        {
            Lease = new HealingOperationLease<ProviderOperation>(
                Guid.NewGuid(), "lease-2", NewOperation(ProviderOperationKind.PublishPullRequest), 1, 2)
        };
        var service = CreateService(store, []);

        var result = await service.RunOnceAsync();

        Assert.Equal(HealingWorkerRunStatus.DeadLettered, result.Status);
        Assert.Equal("provider-operation-handler-not-configured", store.Outcome!.OutcomeCode);
    }

    private static ProviderOperationService CreateService(
        RecordingStore store,
        IReadOnlyList<IProviderOperationHandler> handlers) =>
        new(store, handlers, new HealingOptions
        {
            RepairDispatchEnabled = true,
            RetryDelay = TimeSpan.FromMinutes(1)
        }, "provider-1", new FixedTimeProvider());

    private static ProviderOperation NewOperation(ProviderOperationKind kind) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        ProviderConnectionId = Guid.NewGuid(),
        Kind = kind,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        PayloadJson = "{}",
        PayloadHash = new string('a', 64),
        Status = ProviderOperationStatus.Leased
    };

    private sealed class RetryHandler : IProviderOperationHandler
    {
        public ProviderOperationKind Kind => ProviderOperationKind.DispatchWorkflow;

        public ValueTask<HealingOperationOutcome> ExecuteAsync(ProviderOperation operation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(HealingOperationOutcome.Retry("github-rate-limited"));
    }

    private sealed class RecordingStore : IProviderOperationStore
    {
        public HealingOperationLease<ProviderOperation>? Lease { get; set; }
        public HealingOperationOutcome? Outcome { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }

        public ValueTask<ProviderOperationAppendResult> AppendAsync(ProviderOperation operation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderOperationAppendResult(operation, false));

        public ValueTask<int> RecoverStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<HealingOperationLease<ProviderOperation>?> TryLeaseNextAsync(string workerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Lease);

        public ValueTask FinishAsync(HealingOperationLease<ProviderOperation> lease, HealingOperationOutcome outcome, DateTimeOffset finishedAt, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
        {
            Outcome = outcome;
            NextAttemptAt = nextAttemptAt;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
