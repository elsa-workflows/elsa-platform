using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Providers;
using FluentAssertions;

namespace Elsa.Platform.Healing.Core.Tests.Providers;

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

        result.IsReplay.Should().BeFalse();
        result.Operation.PayloadJson.Should().Be("{\"attempt\":1}");
        result.Operation.PayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
        result.Operation.Status.Should().Be(ProviderOperationStatus.Pending);
        result.Operation.CreatedAt.Should().Be(Now);
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

        result.Status.Should().Be(HealingWorkerRunStatus.RetryScheduled);
        store.Outcome.Should().Be(HealingOperationOutcome.Retry("github-rate-limited"));
        store.NextAttemptAt.Should().Be(Now.AddMinutes(1));
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

        result.Status.Should().Be(HealingWorkerRunStatus.DeadLettered);
        store.Outcome!.OutcomeCode.Should().Be("provider-operation-handler-not-configured");
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
