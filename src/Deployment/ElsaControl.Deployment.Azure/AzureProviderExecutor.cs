using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Orchestrates the durable Azure operation around an injected provider runner. The runner is
/// the only component that may cross the Azure/Bicep execution boundary; this class owns claim,
/// checkpoint, idempotency and recovery semantics.
/// </summary>
public sealed class AzureProviderExecutor
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(15);
    private readonly IAzureProviderOperationStore _store;
    private readonly IAzureProviderRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _heartbeatInterval;
    private readonly string _workerId;

    public AzureProviderExecutor(
        IAzureProviderOperationStore store,
        IAzureProviderRunner runner,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null,
        string? workerId = null,
        TimeSpan? heartbeatInterval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _leaseDuration = leaseDuration ?? DefaultLeaseDuration;
        if (_leaseDuration <= TimeSpan.Zero || _leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The provider lease must be positive and no longer than one hour.");
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval(_leaseDuration);
        if (_heartbeatInterval <= TimeSpan.Zero || _heartbeatInterval >= _leaseDuration)
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), "The heartbeat interval must be positive and shorter than the lease.");
        _workerId = workerId ?? $"azure-executor-{Guid.NewGuid():N}";
        AzureProviderOperationValidation.ValidateWorkerId(_workerId);
    }

    /// <summary>
    /// Applies an accepted Azure workload plan. A completed idempotent operation returns
    /// <see cref="AzureProviderExecutionOutcome.NoOp"/> and does not invoke the runner again.
    /// </summary>
    public Task<AzureProviderExecutionResult> ApplyAsync(
        AzureProviderOperationRequest operationRequest,
        AzureWorkloadPlan plan,
        CancellationToken cancellationToken = default) =>
        ExecuteApplyAsync(operationRequest, plan, cancellationToken);

    private Task<AzureProviderExecutionResult> ExecuteApplyAsync(
        AzureProviderOperationRequest operationRequest,
        AzureWorkloadPlan plan,
        CancellationToken cancellationToken)
    {
        if (operationRequest is null)
            throw new ArgumentNullException(nameof(operationRequest));
        if (operationRequest.Action != AzureProviderOperationAction.Reconcile)
            throw new ArgumentException("Apply operations must use the Reconcile action.", nameof(operationRequest));
        return ExecuteAsync(new AzureProviderExecutionRequest(operationRequest, plan), cancellationToken);
    }

    public Task<AzureProviderExecutionResult> DeleteAsync(
        AzureProviderOperationRequest operationRequest,
        AzureWorkloadPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (operationRequest is null)
            throw new ArgumentNullException(nameof(operationRequest));

        if (operationRequest.Action != AzureProviderOperationAction.Delete)
            throw new ArgumentException("Delete operations must use the Delete action.", nameof(operationRequest));

        return ExecuteAsync(new AzureProviderExecutionRequest(operationRequest, plan), cancellationToken);
    }

    public Task<AzureProviderExecutionResult> ExecuteAsync(
        AzureProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateExecutionRequest(request);
        return ExecuteCoreAsync(request with { Plan = CopySafePlan(request.Plan) }, cancellationToken);
    }

    private async Task<AzureProviderExecutionResult> ExecuteCoreAsync(
        AzureProviderExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var operationRequest = AzureProviderOperationValidation.Normalize(request.Operation);
        AzureProviderOperation operation;
        try
        {
            operation = await _store.CreateOrGetAsync(operationRequest, now, cancellationToken);
        }
        catch (AzureProviderOperationConflictException exception)
        {
            return ResultForObservedState(
                exception.Operation,
                "azure.operation.target-busy",
                "Another Azure operation currently owns this target.");
        }

        if (operation.Status == AzureProviderOperationStatus.Succeeded)
            return Result(operation, AzureProviderExecutionOutcome.NoOp, "azure.operation.no-op", "The Azure workload already matches the requested plan.");
        if (operation.Status is AzureProviderOperationStatus.Failed or AzureProviderOperationStatus.Cancelled)
            return Result(operation, AzureProviderExecutionOutcome.Failed, "azure.operation.terminal", "The Azure operation is already terminal and requires a new idempotency key.");
        if (operation.Status == AzureProviderOperationStatus.Running)
            return Result(operation, AzureProviderExecutionOutcome.InProgress, "azure.operation.in-progress", "The Azure operation is already owned by another worker.");

        var leaseToken = Guid.NewGuid().ToString("N");
        var claimed = operation.Status == AzureProviderOperationStatus.RecoveryRequired
            ? await _store.ClaimRecoveryAsync(operation.WorkspaceId, operation.Id, _workerId, leaseToken, _leaseDuration, now, operation.Version, cancellationToken)
            : await _store.ClaimAsync(operation.WorkspaceId, operation.Id, _workerId, leaseToken, _leaseDuration, now, operation.Version, cancellationToken);
        if (claimed is null)
        {
            var latest = await _store.GetAsync(operation.WorkspaceId, operation.Id, cancellationToken) ?? operation;
            return ResultForObservedState(
                latest,
                "azure.operation.claim-lost",
                "The Azure operation is owned by another worker or changed concurrently.");
        }

        if (claimed.Action == AzureProviderOperationAction.Delete)
        {
            // Delete is a separate idempotent operation from reconcile. Carry the latest
            // provider-owned resource snapshot into it so cleanup cannot silently become a
            // vacuous no-op after a successful apply.
            var latestReconcile = await _store.GetLatestReconcileAsync(
                claimed.WorkspaceId,
                claimed.TargetKey,
                CancellationToken.None);
            if (latestReconcile is not null)
                claimed = claimed with { Resources = latestReconcile.Resources };

            return await ExecuteDeleteAsync(request.Plan, claimed, leaseToken, cancellationToken);
        }

        return await ExecuteReconcileAsync(request.Plan, claimed, leaseToken, cancellationToken);
    }

    private async Task<AzureProviderExecutionResult> ExecuteReconcileAsync(
        AzureWorkloadPlan plan,
        AzureProviderOperation operation,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (operation.Status != AzureProviderOperationStatus.Running)
                return Result(operation, MapOutcome(operation.Status), "azure.operation.state-changed", "The Azure operation changed state before the next lifecycle step.");

            IReadOnlyList<(AzureProviderRunnerStep Step, AzureProviderOperationPhase Phase)> next;
            try
            {
                next = NextReconcileStep(operation.Phase);
            }
            catch (InvalidOperationException)
            {
                return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.Failed, "azure.operation.phase.invalid", "The reconcile operation has an invalid lifecycle phase.");
            }
            if (next.Count == 0)
                return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.Succeeded, "azure.operation.succeeded", "Azure workload reconciliation completed.");

            foreach (var (step, phase) in next)
            {
                if (cancellationToken.IsCancellationRequested)
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step was cancelled before the next remote mutation.");

                var command = new AzureProviderRunnerCommand(
                    step,
                    plan,
                    operation.Resources,
                    operation.Resources.StableTrafficRevisionName,
                    operation.AttemptNumber > 1,
                    operation.AttemptNumber);
                AzureProviderRunnerResult runnerResult;
                try
                {
                    var run = await RunRunnerAsync(command, operation, leaseToken, cancellationToken);
                    runnerResult = run.Result;
                    operation = run.Operation;
                    ValidateRunnerResult(runnerResult, phase, requiresHealthyEndpoint: step == AzureProviderRunnerStep.Promotion);
                }
                catch (RunnerExecutionException exception) when (exception.Cause is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    return await MarkRecoveryAsync(exception.Operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step was interrupted before its result could be confirmed.");
                }
                catch (RunnerExecutionException exception)
                {
                    return await MarkRecoveryAsync(exception.Operation, leaseToken, "azure.step.uncertain", "The Azure lifecycle step failed before its external result could be confirmed.");
                }
                catch (LeaseLostException)
                {
                    return await GetConcurrentResultAsync(operation);
                }
                catch (OperationCanceledException)
                {
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step was interrupted before its result could be confirmed.");
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step was interrupted before its result could be confirmed.");
                }
                catch (Exception)
                {
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.uncertain", "The Azure lifecycle step failed before its external result could be confirmed.");
                }

                if (runnerResult.Outcome is AzureProviderRunnerOutcome.Failed or AzureProviderRunnerOutcome.Uncertain)
                {
                    var failureOperation = await PersistRunnerReferencesAsync(
                        operation,
                        leaseToken,
                        runnerResult,
                        CancellationToken.None,
                        preserveStableTrafficRevision: step == AzureProviderRunnerStep.Promotion);
                    if (failureOperation is null)
                        return await GetConcurrentResultAsync(operation);
                    operation = failureOperation;
                    if (step == AzureProviderRunnerStep.Promotion)
                        return await HandlePromotionFailureAsync(plan, operation, leaseToken, runnerResult, CancellationToken.None);

                    return await FinalizeResultAsync(
                        operation,
                        leaseToken,
                        runnerResult.Outcome == AzureProviderRunnerOutcome.Uncertain ? AzureProviderOperationStatus.RecoveryRequired : AzureProviderOperationStatus.Failed,
                        SafeStepCode(step, runnerResult.Outcome),
                        SafeStepMessage(step, runnerResult.Outcome),
                        CancellationToken.None);
                }

                if (step == AzureProviderRunnerStep.Health &&
                    (runnerResult.Health == AzureProviderHealth.Unknown || string.IsNullOrWhiteSpace(runnerResult.Endpoint)))
                {
                    var incomplete = await PersistRunnerReferencesAsync(operation, leaseToken, runnerResult, CancellationToken.None);
                    if (incomplete is null)
                        return await GetConcurrentResultAsync(operation);
                    return await FinalizeResultAsync(
                        incomplete,
                        leaseToken,
                        AzureProviderOperationStatus.RecoveryRequired,
                        "azure.health.incomplete",
                        "The provider did not return a complete health result for the candidate.",
                        CancellationToken.None);
                }
                if (step == AzureProviderRunnerStep.Health && runnerResult.Health != AzureProviderHealth.Healthy)
                {
                    var unhealthy = await CheckpointAsync(
                        operation,
                        leaseToken,
                        new AzureProviderCheckpoint(
                            phase,
                            "azure.health.unhealthy",
                            "The candidate did not pass health verification.",
                            runnerResult.Resources,
                            runnerResult.Endpoint,
                            runnerResult.Health,
                            SafeDiagnostics(runnerResult.Diagnostics)),
                        CancellationToken.None);
                    if (unhealthy is null)
                        return await GetConcurrentResultAsync(operation);
                    return await FinalizeResultAsync(unhealthy, leaseToken, AzureProviderOperationStatus.Failed, "azure.health.unhealthy", "The candidate did not pass health verification.");
                }

                var checkpoint = new AzureProviderCheckpoint(
                    phase,
                    SafeStepCode(step, runnerResult.Outcome),
                    SafeStepMessage(step, runnerResult.Outcome),
                    runnerResult.Resources,
                    runnerResult.Endpoint,
                    runnerResult.Health,
                    SafeDiagnostics(runnerResult.Diagnostics));
                AzureProviderOperation? checkpointed;
                try
                {
                    checkpointed = await CheckpointAsync(operation, leaseToken, checkpoint, CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step completed but its checkpoint could not be confirmed.");
                }
                if (checkpointed is null)
                    return await GetConcurrentResultAsync(operation);
                operation = checkpointed;
                if (cancellationToken.IsCancellationRequested)
                    return await MarkRecoveryAsync(operation, leaseToken, "azure.step.cancelled", "The Azure lifecycle step completed but the operation was cancelled before the next remote mutation.");
            }
        }
    }

    private async Task<AzureProviderExecutionResult> ExecuteDeleteAsync(
        AzureWorkloadPlan plan,
        AzureProviderOperation operation,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        if (operation.Phase is AzureProviderOperationPhase.Planned)
        {
            var submitted = await CheckpointAsync(
                operation,
                leaseToken,
                new AzureProviderCheckpoint(
                    AzureProviderOperationPhase.CleanupSubmitted,
                    "azure.cleanup.submitted",
                    "Exact owned-resource cleanup was submitted.",
                    operation.Resources,
                    null,
                    AzureProviderHealth.Unknown,
                    []),
                CancellationToken.None);
            if (submitted is null)
                return await GetConcurrentResultAsync(operation);
            operation = submitted;
        }

        if (cancellationToken.IsCancellationRequested)
            return await MarkRecoveryAsync(operation, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup was cancelled before the remote cleanup step.");

        if (operation.Phase == AzureProviderOperationPhase.CleanupVerified)
            return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.Succeeded, "azure.cleanup.succeeded", "Exact owned-resource cleanup was verified.");
        if (operation.Phase != AzureProviderOperationPhase.CleanupSubmitted)
            return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.Failed, "azure.cleanup.phase.invalid", "The delete operation has an invalid cleanup phase.");

        AzureProviderRunnerResult runnerResult;
        try
        {
            var command = new AzureProviderRunnerCommand(
                AzureProviderRunnerStep.Cleanup,
                plan,
                operation.Resources,
                operation.Resources.StableTrafficRevisionName,
                operation.AttemptNumber > 1,
                operation.AttemptNumber);
            var run = await RunRunnerAsync(command, operation, leaseToken, cancellationToken);
            runnerResult = run.Result;
            operation = run.Operation;
            ValidateRunnerResult(runnerResult, AzureProviderOperationPhase.CleanupVerified, requiresHealthyEndpoint: false);
        }
        catch (RunnerExecutionException exception) when (exception.Cause is OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            return await MarkRecoveryAsync(exception.Operation, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup was interrupted before absence could be confirmed.");
        }
        catch (RunnerExecutionException exception)
        {
            return await MarkRecoveryAsync(exception.Operation, leaseToken, "azure.cleanup.uncertain", "Owned-resource cleanup failed before absence could be confirmed.");
        }
        catch (LeaseLostException)
        {
            return await GetConcurrentResultAsync(operation);
        }
        catch (OperationCanceledException)
        {
            return await MarkRecoveryAsync(operation, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup was interrupted before absence could be confirmed.");
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return await MarkRecoveryAsync(operation, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup was interrupted before absence could be confirmed.");
        }
        catch (Exception)
        {
            return await MarkRecoveryAsync(operation, leaseToken, "azure.cleanup.uncertain", "Owned-resource cleanup failed before absence could be confirmed.");
        }

        if (runnerResult.Outcome is AzureProviderRunnerOutcome.Failed or AzureProviderRunnerOutcome.Uncertain)
        {
            var failureOperation = await PersistRunnerReferencesAsync(operation, leaseToken, runnerResult, CancellationToken.None);
            if (failureOperation is null)
                return await GetConcurrentResultAsync(operation);
            operation = failureOperation;
            return await FinalizeResultAsync(
                operation,
                leaseToken,
                runnerResult.Outcome == AzureProviderRunnerOutcome.Uncertain ? AzureProviderOperationStatus.RecoveryRequired : AzureProviderOperationStatus.Failed,
                SafeStepCode(AzureProviderRunnerStep.Cleanup, runnerResult.Outcome),
                SafeStepMessage(AzureProviderRunnerStep.Cleanup, runnerResult.Outcome),
                CancellationToken.None);
        }

        if (!runnerResult.OwnedResourcesAbsent || runnerResult.Resources != new AzureProviderResourceReferences() || runnerResult.Endpoint is not null || runnerResult.Health != AzureProviderHealth.Unknown)
        {
            return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.Failed, "azure.cleanup.ownership.unverified", "The provider did not prove exact owned-resource absence.");
        }

        AzureProviderOperation? verified;
        try
        {
            verified = await CheckpointAsync(
                operation,
                leaseToken,
                new AzureProviderCheckpoint(
                    AzureProviderOperationPhase.CleanupVerified,
                    SafeStepCode(AzureProviderRunnerStep.Cleanup, runnerResult.Outcome),
                    SafeStepMessage(AzureProviderRunnerStep.Cleanup, runnerResult.Outcome),
                    new(),
                    null,
                    AzureProviderHealth.Unknown,
                    SafeDiagnostics(runnerResult.Diagnostics),
                    ReplaceResources: true),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return await MarkRecoveryAsync(operation, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup completed but its absence checkpoint could not be confirmed.");
        }
        if (verified is null)
            return await GetConcurrentResultAsync(operation);
        try
        {
            return await FinalizeResultAsync(verified, leaseToken, AzureProviderOperationStatus.Succeeded, "azure.cleanup.succeeded", "Exact owned-resource cleanup was verified.", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return await MarkRecoveryAsync(verified, leaseToken, "azure.cleanup.cancelled", "Owned-resource cleanup completed but its final state could not be confirmed.");
        }
    }

    private async Task<AzureProviderExecutionResult> HandlePromotionFailureAsync(
        AzureWorkloadPlan plan,
        AzureProviderOperation operation,
        string leaseToken,
        AzureProviderRunnerResult promotion,
        CancellationToken cancellationToken)
    {
        // A first deployment has no previously verified revision to restore. Do not ask the
        // runner to invent one: an uncertain promotion without a stable revision must remain in
        // durable recovery until an operator can establish the traffic state.
        if (string.IsNullOrWhiteSpace(operation.Resources.StableTrafficRevisionName))
        {
            return await MarkRecoveryAsync(
                operation,
                leaseToken,
                promotion.Outcome == AzureProviderRunnerOutcome.Uncertain
                    ? "azure.promotion.uncertain"
                    : "azure.promotion.rollback-unavailable",
                promotion.Outcome == AzureProviderRunnerOutcome.Uncertain
                    ? "Candidate promotion was uncertain and no previously verified stable traffic revision was available."
                    : "Candidate promotion failed and no previously verified stable traffic revision was available to restore.");
        }

        var rollbackCommand = new AzureProviderRunnerCommand(
            AzureProviderRunnerStep.RestoreStableTraffic,
            plan,
            operation.Resources,
            operation.Resources.StableTrafficRevisionName,
            operation.AttemptNumber > 1,
            operation.AttemptNumber);
        try
        {
            var run = await RunRunnerAsync(rollbackCommand, operation, leaseToken, cancellationToken);
            var rollback = run.Result;
            operation = run.Operation;
            ValidateRunnerResult(rollback, AzureProviderOperationPhase.HealthVerified, requiresHealthyEndpoint: false);
            if ((rollback.Outcome == AzureProviderRunnerOutcome.Completed || rollback.Outcome == AzureProviderRunnerOutcome.NoOp) &&
                rollback.StableTrafficRestored &&
                string.Equals(rollback.Resources.StableTrafficRevisionName, operation.Resources.StableTrafficRevisionName, StringComparison.Ordinal))
            {
                var restored = await CheckpointAsync(
                    operation,
                    leaseToken,
                    new AzureProviderCheckpoint(
                        AzureProviderOperationPhase.HealthVerified,
                        "azure.promotion.restored",
                        "Stable traffic was restored after candidate promotion failed.",
                        rollback.Resources,
                        rollback.Endpoint,
                        rollback.Health,
                        SafeDiagnostics(rollback.Diagnostics)),
                    CancellationToken.None);
                if (restored is not null)
                    operation = restored;
                return await FinalizeResultAsync(
                    operation,
                    leaseToken,
                    promotion.Outcome == AzureProviderRunnerOutcome.Uncertain
                        ? AzureProviderOperationStatus.RecoveryRequired
                        : AzureProviderOperationStatus.Failed,
                    promotion.Outcome == AzureProviderRunnerOutcome.Uncertain ? "azure.promotion.uncertain" : SafeStepCode(AzureProviderRunnerStep.Promotion, promotion.Outcome),
                    promotion.Outcome == AzureProviderRunnerOutcome.Uncertain
                        ? "Candidate promotion was uncertain; stable traffic was restored but operator recovery is required."
                        : SafeStepMessage(AzureProviderRunnerStep.Promotion, promotion.Outcome),
                    CancellationToken.None);
            }
        }
        catch (RunnerExecutionException exception)
        {
            operation = exception.Operation;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled rollback is still an uncertain external operation and is recorded below.
        }
        catch (Exception)
        {
            // The durable recovery state is the safe result when rollback itself is not confirmed.
        }

        return await MarkRecoveryAsync(operation, leaseToken, "azure.promotion.rollback-uncertain", "Candidate promotion and stable-traffic restoration could not both be confirmed.");
    }

    private async Task<AzureProviderOperation?> CheckpointAsync(
        AzureProviderOperation operation,
        string leaseToken,
        AzureProviderCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        AzureProviderOperationValidation.ValidateCheckpoint(checkpoint);
        return await _store.CheckpointAsync(
            operation.WorkspaceId,
            operation.Id,
            leaseToken,
            checkpoint,
            _timeProvider.GetUtcNow(),
            operation.Version,
            cancellationToken);
    }

    private async Task<AzureProviderOperation?> PersistRunnerReferencesAsync(
        AzureProviderOperation operation,
        string leaseToken,
        AzureProviderRunnerResult runnerResult,
        CancellationToken cancellationToken,
        bool preserveStableTrafficRevision = false)
    {
        // A failed or uncertain provider result can still contain the only durable handles to
        // resources created before the result was produced. Keep the current phase monotonic and
        // merge these handles in the store so recovery and delete can continue safely.
        var resources = preserveStableTrafficRevision
            ? runnerResult.Resources with { StableTrafficRevisionName = null }
            : runnerResult.Resources;
        // Reference-only checkpoints are partial by contract. A runner may omit observations when
        // a lifecycle step does not own them, so preserve the last authoritative values rather
        // than turning an omitted endpoint/Unknown health into a destructive overwrite.
        var endpoint = runnerResult.Endpoint ?? operation.Endpoint;
        var health = runnerResult.Health == AzureProviderHealth.Unknown ? operation.Health : runnerResult.Health;
        return await CheckpointAsync(
            operation,
            leaseToken,
            new AzureProviderCheckpoint(
                operation.Phase,
                "azure.step.references",
                "Provider resource references were retained for recovery.",
                resources,
                endpoint,
                health,
                SafeDiagnostics(runnerResult.Diagnostics)),
            cancellationToken);
    }

    private async Task<AzureProviderExecutionResult> MarkRecoveryAsync(
        AzureProviderOperation operation,
        string leaseToken,
        string code,
        string message)
    {
        return await FinalizeResultAsync(operation, leaseToken, AzureProviderOperationStatus.RecoveryRequired, code, message, CancellationToken.None);
    }

    private async Task<AzureProviderExecutionResult> FinalizeResultAsync(
        AzureProviderOperation operation,
        string leaseToken,
        AzureProviderOperationStatus status,
        string code,
        string message,
        CancellationToken cancellationToken = default)
    {
        AzureProviderOperationValidation.ValidateCode(code);
        var finalized = await _store.FinalizeAsync(
            operation.WorkspaceId,
            operation.Id,
            leaseToken,
            status,
            code,
            _timeProvider.GetUtcNow(),
            operation.Version,
            cancellationToken);
        if (finalized is null)
            return await GetConcurrentResultAsync(operation);
        return Result(finalized, MapOutcome(finalized.Status), code, message);
    }

    private async Task<AzureProviderExecutionResult> GetConcurrentResultAsync(AzureProviderOperation operation)
    {
        var latest = await _store.GetAsync(operation.WorkspaceId, operation.Id, CancellationToken.None) ?? operation;
        return ResultForObservedState(
            latest,
            "azure.operation.concurrent-update",
            "The Azure operation changed concurrently and must be observed by its current owner.");
    }

    private async Task<(AzureProviderRunnerResult Result, AzureProviderOperation Operation)> RunRunnerAsync(
        AzureProviderRunnerCommand command,
        AzureProviderOperation operation,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var runnerTask = _runner.RunAsync(command, runnerCancellation.Token);
            while (true)
            {
                var delayTask = Task.Delay(_heartbeatInterval, cancellationToken);
                var completed = await Task.WhenAny(runnerTask, delayTask);
                if (completed == runnerTask)
                    return (await runnerTask, operation);

                cancellationToken.ThrowIfCancellationRequested();
                var renewed = await _store.HeartbeatAsync(
                    operation.WorkspaceId,
                    operation.Id,
                    leaseToken,
                    _leaseDuration,
                    _timeProvider.GetUtcNow(),
                    operation.Version,
                    cancellationToken);
                if (renewed is null)
                {
                    try
                    {
                        runnerCancellation.Cancel();
                    }
                    catch (AggregateException)
                    {
                        // Cancellation is best-effort. The runner contract still requires
                        // every remote step to be idempotent when the external job cannot stop.
                    }
                    _ = runnerTask.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
                    throw new LeaseLostException();
                }
                operation = renewed;
            }
        }
        catch (LeaseLostException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RunnerExecutionException(operation, exception);
        }
    }

    private static IReadOnlyList<(AzureProviderRunnerStep Step, AzureProviderOperationPhase Phase)> NextReconcileStep(AzureProviderOperationPhase phase) =>
        phase switch
        {
            AzureProviderOperationPhase.Planned or AzureProviderOperationPhase.FoundationSubmitted =>
                phase == AzureProviderOperationPhase.Planned
                    ? [(AzureProviderRunnerStep.Foundation, AzureProviderOperationPhase.FoundationSubmitted)]
                    : [
                        (AzureProviderRunnerStep.AcrPull, AzureProviderOperationPhase.FoundationSubmitted),
                        (AzureProviderRunnerStep.SeedSecrets, AzureProviderOperationPhase.FoundationSubmitted),
                        (AzureProviderRunnerStep.SqlBootstrap, AzureProviderOperationPhase.FoundationReady)
                    ],
            AzureProviderOperationPhase.FoundationReady or AzureProviderOperationPhase.WorkloadSubmitted =>
                [(AzureProviderRunnerStep.Workload, AzureProviderOperationPhase.WorkloadReady)],
            AzureProviderOperationPhase.WorkloadReady =>
                [(AzureProviderRunnerStep.Health, AzureProviderOperationPhase.HealthVerified)],
            AzureProviderOperationPhase.HealthVerified =>
                [(AzureProviderRunnerStep.Promotion, AzureProviderOperationPhase.TrafficPromoted)],
            AzureProviderOperationPhase.TrafficPromoted => [],
            _ => throw new InvalidOperationException("The reconcile operation has an invalid lifecycle phase.")
        };

    private static void ValidateRunnerResult(AzureProviderRunnerResult result, AzureProviderOperationPhase expectedPhase, bool requiresHealthyEndpoint)
    {
        if (result is null)
            throw new ArgumentException("The provider runner returned no result.", nameof(result));
        if (!Enum.IsDefined(result.Outcome) || !Enum.IsDefined(result.Phase) || !Enum.IsDefined(result.Health))
            throw new ArgumentException("The provider runner returned an invalid result enum.", nameof(result));
        AzureProviderOperationValidation.ValidateCode(result.Code);
        AzureProviderOperationValidation.ValidateMessage(result.Message);
        AzureProviderOperationValidation.ValidateReferences(result.Resources);
        AzureProviderOperationValidation.ValidateEndpoint(result.Endpoint);
        if (result.Diagnostics is null || result.Diagnostics.Count > 20)
            throw new ArgumentException("The provider runner returned unbounded diagnostics.", nameof(result));
        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic is null)
                throw new ArgumentException("The provider runner returned a null diagnostic.", nameof(result));
            AzureProviderOperationValidation.ValidateCode(diagnostic.Code);
            if (diagnostic.Message is null || diagnostic.Message.Length > 2000)
                throw new ArgumentException("The provider runner returned an unbounded diagnostic.", nameof(result));
        }

        if (result.Outcome is AzureProviderRunnerOutcome.Completed or AzureProviderRunnerOutcome.NoOp)
        {
            if (result.Phase != expectedPhase)
                throw new ArgumentException("The provider runner completed a different lifecycle phase.", nameof(result));
            if (requiresHealthyEndpoint && (result.Health != AzureProviderHealth.Healthy || string.IsNullOrWhiteSpace(result.Endpoint)))
                throw new ArgumentException("A successful promotion step must return a healthy HTTPS endpoint.", nameof(result));
        }
    }

    private static string SafeStepMessage(AzureProviderRunnerStep step, AzureProviderRunnerOutcome outcome) =>
        outcome switch
        {
            AzureProviderRunnerOutcome.Completed => $"Azure {StepLabel(step)} step completed.",
            AzureProviderRunnerOutcome.NoOp => $"Azure {StepLabel(step)} step was already converged.",
            AzureProviderRunnerOutcome.Failed => $"Azure {StepLabel(step)} step failed.",
            AzureProviderRunnerOutcome.Uncertain => $"Azure {StepLabel(step)} step requires recovery.",
            _ => "Azure provider step returned an invalid outcome."
        };

    private static string SafeStepCode(AzureProviderRunnerStep step, AzureProviderRunnerOutcome outcome) =>
        outcome switch
        {
            AzureProviderRunnerOutcome.Completed => "azure.step.completed",
            AzureProviderRunnerOutcome.NoOp => "azure.step.no-op",
            AzureProviderRunnerOutcome.Failed => "azure.step.failed",
            AzureProviderRunnerOutcome.Uncertain => "azure.step.uncertain",
            _ => "azure.step.invalid"
        };

    private static string StepLabel(AzureProviderRunnerStep step) =>
        step switch
        {
            AzureProviderRunnerStep.Foundation => "foundation",
            AzureProviderRunnerStep.AcrPull => "registry access",
            AzureProviderRunnerStep.SeedSecrets => "credential reference seeding",
            AzureProviderRunnerStep.SqlBootstrap => "database bootstrap",
            AzureProviderRunnerStep.Workload => "workload",
            AzureProviderRunnerStep.Health => "health verification",
            AzureProviderRunnerStep.Promotion => "traffic promotion",
            AzureProviderRunnerStep.RestoreStableTraffic => "stable traffic restoration",
            AzureProviderRunnerStep.Cleanup => "cleanup",
            _ => "provider"
        };

    private static IReadOnlyList<AzureProviderDiagnostic> SafeDiagnostics(IReadOnlyList<AzureProviderDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => new AzureProviderDiagnostic(diagnostic.Code, diagnostic.Code)).ToArray();

    private sealed class LeaseLostException : Exception;

    private sealed class RunnerExecutionException(AzureProviderOperation operation, Exception cause) : Exception(cause.Message, cause)
    {
        public AzureProviderOperation Operation { get; } = operation;
        public Exception Cause { get; } = cause;
    }

    private static void ValidateExecutionRequest(AzureProviderExecutionRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Operation is null)
            throw new ArgumentNullException(nameof(request.Operation));
        if (request.Plan is null)
            throw new ArgumentNullException(nameof(request.Plan));

        var operation = AzureProviderOperationValidation.Normalize(request.Operation);
        var plan = request.Plan;
        if (!IsFingerprint(plan.Fingerprint) || !string.Equals(plan.Fingerprint, operation.PlanFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The provider plan fingerprint does not match the operation request.", nameof(request));
        if (string.IsNullOrWhiteSpace(plan.WorkloadName) || plan.WorkloadName.Length is < 3 or > 16 ||
            !char.IsAsciiLetterOrDigit(plan.WorkloadName[0]) || !char.IsAsciiLetterOrDigit(plan.WorkloadName[^1]) ||
            plan.WorkloadName.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-'))
            throw new ArgumentException("The provider plan workload name is required.", nameof(request));
        if (!string.Equals(plan.Location, operation.Location, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ElsaVersion, operation.ElsaVersion, StringComparison.Ordinal) ||
            !string.Equals(plan.ReleaseLine, operation.ReleaseLine, StringComparison.Ordinal) ||
            !string.Equals(plan.Topology, operation.Topology, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.Isolation, operation.Isolation, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ImageRepository, operation.ImageRepository, StringComparison.Ordinal) ||
            !string.Equals($"sha256:{plan.ImageDigest}", operation.ImageDigest, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ReleaseManifestDigest, operation.ReleaseManifestDigest, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ReleaseManifestSignatureDigest, operation.ReleaseManifestSignatureDigest, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The provider plan does not match the operation request.", nameof(request));

        if (!AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(plan.ReleaseManifestReference, plan.ReleaseManifestDigest) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(plan.ReleaseManifestSignatureReference, plan.ReleaseManifestSignatureDigest))
            throw new ArgumentException("Provider evidence references must be safe immutable locators.", nameof(request));
        if (!IsSha256Digest(plan.ReleaseManifestDigest) || !IsSha256Digest(plan.ReleaseManifestSignatureDigest))
            throw new ArgumentException("The provider plan must include verified release-manifest digests.", nameof(request));
        if (plan.ImageDigest.Length != 64 || !plan.ImageDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("The provider plan image digest must be exactly 64 hexadecimal characters.", nameof(request));
        if (!plan.ImageRepository.StartsWith($"{AzureWorkloadPlanTranslator.SupportedRegistryHost}/", StringComparison.Ordinal))
            throw new ArgumentException("The provider plan image must use the governed Azure registry authority.", nameof(request));

        if (plan.SecretReferences is null || plan.SecretReferences.Count > 64)
            throw new ArgumentException("Secret references are required and bounded.", nameof(request));
        foreach (var pair in plan.SecretReferences)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Key.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 512 || pair.Value.Any(char.IsControl) ||
                !Regex.IsMatch(pair.Value, "^secret://[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
                throw new ArgumentException("Secret references must be safe provider locators.", nameof(request));
        }
    }

    private static AzureProviderExecutionOutcome MapOutcome(AzureProviderOperationStatus status) =>
        status switch
        {
            AzureProviderOperationStatus.Succeeded => AzureProviderExecutionOutcome.Succeeded,
            AzureProviderOperationStatus.Failed or AzureProviderOperationStatus.Cancelled => AzureProviderExecutionOutcome.Failed,
            AzureProviderOperationStatus.RecoveryRequired => AzureProviderExecutionOutcome.RecoveryRequired,
            _ => AzureProviderExecutionOutcome.InProgress
        };

    private static AzureProviderExecutionResult Result(AzureProviderOperation operation, AzureProviderExecutionOutcome outcome, string code, string message)
    {
        AzureProviderOperationValidation.ValidateCode(code);
        AzureProviderOperationValidation.ValidateMessage(message);
        return new(operation, outcome, code, message);
    }

    private static AzureProviderExecutionResult ResultForObservedState(
        AzureProviderOperation operation,
        string inProgressCode,
        string inProgressMessage) => operation.Status switch
        {
            AzureProviderOperationStatus.Succeeded =>
                Result(operation, AzureProviderExecutionOutcome.NoOp, "azure.operation.no-op", "The Azure workload already matches the requested plan."),
            AzureProviderOperationStatus.Failed or AzureProviderOperationStatus.Cancelled =>
                Result(operation, AzureProviderExecutionOutcome.Failed, "azure.operation.terminal", "The Azure operation is terminal and requires a new idempotency key."),
            AzureProviderOperationStatus.RecoveryRequired =>
                Result(operation, AzureProviderExecutionOutcome.RecoveryRequired, "azure.operation.recovery-required", "The Azure operation requires explicit provider recovery."),
            _ => Result(operation, AzureProviderExecutionOutcome.InProgress, inProgressCode, inProgressMessage)
        };

    private static bool IsSha256Digest(string? value) =>
        value is not null && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        value.Length == "sha256:".Length + 64 && value["sha256:".Length..].All(Uri.IsHexDigit);

    private static bool IsFingerprint(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static TimeSpan DefaultHeartbeatInterval(TimeSpan leaseDuration)
    {
        var ticks = Math.Min(TimeSpan.FromMinutes(1).Ticks, leaseDuration.Ticks / 3);
        return TimeSpan.FromTicks(Math.Min(Math.Max(1, ticks), leaseDuration.Ticks - 1));
    }

    private static AzureWorkloadPlan CopySafePlan(AzureWorkloadPlan plan)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in plan.SecretReferences)
        {
            if (!references.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException("Secret references must have unique keys.", nameof(plan));
        }

        return plan with { SecretReferences = new ReadOnlyDictionary<string, string>(references) };
    }
}
