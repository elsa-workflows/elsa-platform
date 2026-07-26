using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Operations;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ValenceControl.Healing.Core.Tests;

public sealed class HealingOperationWorkerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task RunOnceRecoversExpiredLeasesBeforeClaimingNewWork()
    {
        var store = new RecordingStore { RecoveredCount = 2 };
        var worker = CreateWorker(store, new CompletedHandler());

        var result = await worker.RunOnceAsync();

        result.Status.Should().Be(HealingWorkerRunStatus.Idle);
        result.RecoveredLeaseCount.Should().Be(2);
        store.Calls.Should().Equal("recover", "lease");
    }

    [Fact]
    public async Task RunOnceCompletesTheClaimUsingTheSameLease()
    {
        var lease = new HealingOperationLease<TestOperation>(Guid.NewGuid(), "lease-1", new TestOperation("work"), 1, 2);
        var store = new RecordingStore { NextLease = lease };
        var worker = CreateWorker(store, new CompletedHandler());

        var result = await worker.RunOnceAsync();

        result.Should().Be(new HealingWorkerRunResult(HealingWorkerRunStatus.Completed, 0, lease.OperationId, "ok"));
        store.FinishedLease.Should().BeSameAs(lease);
        store.FinishedOutcome.Should().Be(HealingOperationOutcome.Completed("ok"));
        store.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task HandlerFailuresDeadLetterAtTheLeaseAttemptLimitWithoutPersistingExceptionText()
    {
        var store = new RecordingStore
        {
            NextLease = new HealingOperationLease<TestOperation>(Guid.NewGuid(), "lease-2", new TestOperation("work"), 2, 2)
        };
        var worker = CreateWorker(store, new ThrowingHandler());

        var result = await worker.RunOnceAsync();

        result.Status.Should().Be(HealingWorkerRunStatus.DeadLettered);
        store.FinishedOutcome!.OutcomeCode.Should().Be("attempt-limit-reached");
        store.FinishedOutcome.SafeDetail.Should().NotContain("credential-value");
    }

    [Fact]
    public async Task HandlerIsCancelledAndRetriedBeforeItsLeaseCanExpire()
    {
        var store = new RecordingStore
        {
            NextLease = new HealingOperationLease<TestOperation>(Guid.NewGuid(), "lease-3", new TestOperation("work"), 1, 2)
        };
        var options = new HealingOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(5),
            LeaseSafetyMargin = TimeSpan.FromMilliseconds(4_990)
        };
        var worker = CreateWorker(store, new NeverCompletingHandler(), options);

        var result = await worker.RunOnceAsync();

        result.Status.Should().Be(HealingWorkerRunStatus.RetryScheduled);
        store.FinishedOutcome!.OutcomeCode.Should().Be("operation-lease-deadline");
        store.NextAttemptAt.Should().Be(Now.Add(options.RetryDelay));
    }

    [Fact]
    public async Task ContinuousWorkerRetriesAfterTransientStoreFailure()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new RecordingStore
        {
            RecoverFailureCount = 1,
            OnRecovered = () => cancellation.Cancel()
        };
        var options = new HealingOptions { IdleDelay = TimeSpan.FromMilliseconds(100) };
        var worker = CreateWorker(store, new CompletedHandler(), options);

        await worker.RunContinuouslyAsync(cancellation.Token);

        store.RecoverCallCount.Should().Be(2);
    }

    [Fact]
    public async Task LongLivedWorkerStopsLeasingWhenControlKillSwitchReloads()
    {
        var store = new RecordingStore();
        var monitor = new MutableOptionsMonitor(new HealingOptions());
        var worker = new HealingOperationWorker<TestOperation>(
            store,
            new CompletedHandler(),
            monitor,
            "worker-1",
            new FixedTimeProvider(Now));

        (await worker.RunOnceAsync()).Status.Should().Be(HealingWorkerRunStatus.Idle);
        monitor.CurrentValue = new HealingOptions { ControlKillSwitch = true };
        (await worker.RunOnceAsync()).Status.Should().Be(HealingWorkerRunStatus.Paused);

        store.LeaseCallCount.Should().Be(1);
    }

    private static HealingOperationWorker<TestOperation> CreateWorker(
        RecordingStore store,
        IHealingOperationHandler<TestOperation> handler,
        HealingOptions? options = null) =>
        new(store, handler, options ?? new HealingOptions(), "worker-1", new FixedTimeProvider(Now));

    private sealed record TestOperation(string Value);

    private sealed class CompletedHandler : IHealingOperationHandler<TestOperation>
    {
        public ValueTask<HealingOperationOutcome> ExecuteAsync(TestOperation operation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(HealingOperationOutcome.Completed("ok"));
    }

    private sealed class ThrowingHandler : IHealingOperationHandler<TestOperation>
    {
        public ValueTask<HealingOperationOutcome> ExecuteAsync(TestOperation operation, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("credential-value");
    }

    private sealed class NeverCompletingHandler : IHealingOperationHandler<TestOperation>
    {
        public async ValueTask<HealingOperationOutcome> ExecuteAsync(TestOperation operation, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return HealingOperationOutcome.Completed("unreachable");
        }
    }

    private sealed class RecordingStore : IHealingLeasedOperationStore<TestOperation>
    {
        public int RecoveredCount { get; set; }
        public int RecoverFailureCount { get; set; }
        public int RecoverCallCount { get; private set; }
        public int LeaseCallCount { get; private set; }
        public Action? OnRecovered { get; set; }
        public HealingOperationLease<TestOperation>? NextLease { get; set; }
        public HealingOperationLease<TestOperation>? FinishedLease { get; private set; }
        public HealingOperationOutcome? FinishedOutcome { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }
        public List<string> Calls { get; } = [];

        public ValueTask<int> RecoverStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Calls.Add("recover");
            RecoverCallCount++;
            if (RecoverCallCount <= RecoverFailureCount)
                throw new InvalidOperationException("connection-string-with-secret");
            OnRecovered?.Invoke();
            return ValueTask.FromResult(RecoveredCount);
        }

        public ValueTask<HealingOperationLease<TestOperation>?> TryLeaseNextAsync(string workerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Calls.Add("lease");
            LeaseCallCount++;
            return ValueTask.FromResult(NextLease);
        }

        public ValueTask FinishAsync(HealingOperationLease<TestOperation> lease, HealingOperationOutcome outcome, DateTimeOffset finishedAt, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
        {
            Calls.Add("finish");
            FinishedLease = lease;
            FinishedOutcome = outcome;
            NextAttemptAt = nextAttemptAt;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableOptionsMonitor(HealingOptions value) : IOptionsMonitor<HealingOptions>
    {
        public HealingOptions CurrentValue { get; set; } = value;
        public HealingOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HealingOptions, string?> listener) => null;
    }
}
