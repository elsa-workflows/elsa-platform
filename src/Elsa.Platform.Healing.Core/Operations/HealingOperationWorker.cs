using Elsa.Platform.Healing.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Healing.Core.Operations;

public enum HealingOperationDisposition { Completed, Retry, DeadLettered }
public enum HealingWorkerRunStatus { Idle, Completed, RetryScheduled, DeadLettered, Paused }

public sealed record HealingOperationOutcome(
    HealingOperationDisposition Disposition,
    string OutcomeCode,
    string? SafeDetail = null)
{
    public static HealingOperationOutcome Completed(string outcomeCode, string? safeDetail = null) =>
        new(HealingOperationDisposition.Completed, outcomeCode, safeDetail);

    public static HealingOperationOutcome Retry(string outcomeCode, string? safeDetail = null) =>
        new(HealingOperationDisposition.Retry, outcomeCode, safeDetail);

    public static HealingOperationOutcome DeadLettered(string outcomeCode, string? safeDetail = null) =>
        new(HealingOperationDisposition.DeadLettered, outcomeCode, safeDetail);
}

public sealed record HealingOperationLease<T>(
    Guid OperationId,
    string LeaseToken,
    T Operation,
    int AttemptCount,
    int AttemptLimit)
    where T : class;

public sealed record HealingWorkerRunResult(
    HealingWorkerRunStatus Status,
    int RecoveredLeaseCount,
    Guid? OperationId = null,
    string? OutcomeCode = null);

/// <summary>
/// Atomic persistence seam for any Healing operation that uses durable leases. Implementations own compare-and-swap
/// lease checks; FinishAsync must reject an expired or mismatched lease token.
/// </summary>
public interface IHealingLeasedOperationStore<T> where T : class
{
    ValueTask<int> RecoverStaleLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<HealingOperationLease<T>?> TryLeaseNextAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask FinishAsync(
        HealingOperationLease<T> lease,
        HealingOperationOutcome outcome,
        DateTimeOffset finishedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}

public interface IHealingOperationHandler<in T> where T : class
{
    /// <remarks>
    /// Implementations must honor cancellation and make external mutations idempotent. The worker cancels execution
    /// before the operation lease expires; a handler that ignores cancellation may continue without lease authority.
    /// </remarks>
    ValueTask<HealingOperationOutcome> ExecuteAsync(
        T operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reusable one-operation-at-a-time lease runner. Hosted adapters may call RunContinuouslyAsync; deterministic tests
/// and scheduled jobs can call RunOnceAsync through the same interface.
/// </summary>
public sealed class HealingOperationWorker<T> where T : class
{
    private readonly IHealingLeasedOperationStore<T> _store;
    private readonly IHealingOperationHandler<T> _handler;
    private readonly Func<HealingOptions> _getOptions;
    private readonly string _workerId;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HealingOperationWorker<T>> _logger;

    public HealingOperationWorker(
        IHealingLeasedOperationStore<T> store,
        IHealingOperationHandler<T> handler,
        HealingOptions options,
        string workerId,
        TimeProvider? timeProvider = null,
        ILogger<HealingOperationWorker<T>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));

        options.Validate();
        _store = store;
        _handler = handler;
        _getOptions = () => options;
        _workerId = workerId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<HealingOperationWorker<T>>.Instance;
    }

    public HealingOperationWorker(
        IHealingLeasedOperationStore<T> store,
        IHealingOperationHandler<T> handler,
        IOptionsMonitor<HealingOptions> options,
        string workerId,
        TimeProvider? timeProvider = null,
        ILogger<HealingOperationWorker<T>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));

        options.CurrentValue.Validate();
        _store = store;
        _handler = handler;
        _getOptions = () => options.CurrentValue;
        _workerId = workerId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<HealingOperationWorker<T>>.Instance;
    }

    public async ValueTask<HealingWorkerRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var options = GetCurrentOptions();
        var now = _timeProvider.GetUtcNow();
        var recovered = await _store.RecoverStaleLeasesAsync(now, cancellationToken);

        if (options.PlatformKillSwitch)
            return new HealingWorkerRunResult(HealingWorkerRunStatus.Paused, recovered);

        var lease = await _store.TryLeaseNextAsync(_workerId, now, options.LeaseDuration, cancellationToken);
        if (lease is null)
            return new HealingWorkerRunResult(HealingWorkerRunStatus.Idle, recovered);

        HealingOperationOutcome outcome;
        using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<HealingOperationOutcome>? execution = null;
        try
        {
            execution = _handler.ExecuteAsync(lease.Operation, handlerCancellation.Token).AsTask();
            outcome = await execution.WaitAsync(
                options.LeaseDuration - options.LeaseSafetyMargin,
                _timeProvider,
                cancellationToken);
            ArgumentNullException.ThrowIfNull(outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException) when (execution is { IsCompleted: false })
        {
            await handlerCancellation.CancelAsync();
            ObserveLateFault(execution);
            outcome = HealingOperationOutcome.Retry(
                "operation-lease-deadline",
                "The operation handler did not complete within the lease safety deadline.");
        }
        catch
        {
            outcome = HealingOperationOutcome.Retry("operation-handler-failed", "The operation handler failed.");
        }

        if (outcome.Disposition == HealingOperationDisposition.Retry && lease.AttemptCount >= lease.AttemptLimit)
            outcome = HealingOperationOutcome.DeadLettered("attempt-limit-reached", "The operation exhausted its bounded attempt limit.");

        var finishedAt = _timeProvider.GetUtcNow();
        DateTimeOffset? nextAttemptAt = outcome.Disposition == HealingOperationDisposition.Retry
            ? finishedAt.Add(options.RetryDelay)
            : null;
        await _store.FinishAsync(lease, outcome, finishedAt, nextAttemptAt, cancellationToken);

        return new HealingWorkerRunResult(
            ToRunStatus(outcome.Disposition),
            recovered,
            lease.OperationId,
            outcome.OutcomeCode);
    }

    public async Task RunContinuouslyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await RunOnceAsync(cancellationToken);
                if (result.Status is HealingWorkerRunStatus.Idle or HealingWorkerRunStatus.Paused)
                    await Task.Delay(GetCurrentOptions().IdleDelay, _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Deliberately omit the exception object: persistence exceptions may contain credentials or payloads.
                _logger.LogWarning(
                    "Healing worker {WorkerId} iteration failed with code {FailureCode}; retrying after the configured idle delay.",
                    _workerId,
                    "operation-store-unavailable");
                await Task.Delay(GetSafeFailureDelay(), _timeProvider, cancellationToken);
            }
        }
    }

    private HealingOptions GetCurrentOptions()
    {
        var options = _getOptions();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    private TimeSpan GetSafeFailureDelay()
    {
        try
        {
            return GetCurrentOptions().IdleDelay;
        }
        catch
        {
            return TimeSpan.FromSeconds(2);
        }
    }

    private static HealingWorkerRunStatus ToRunStatus(HealingOperationDisposition disposition) => disposition switch
    {
        HealingOperationDisposition.Completed => HealingWorkerRunStatus.Completed,
        HealingOperationDisposition.Retry => HealingWorkerRunStatus.RetryScheduled,
        HealingOperationDisposition.DeadLettered => HealingWorkerRunStatus.DeadLettered,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null)
    };

    private static void ObserveLateFault(Task? task)
    {
        if (task is null || task.IsCompletedSuccessfully)
            return;

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
