namespace ValenceControl.Workflows.RuntimeApplier;

public enum WorkflowRuntimeCommandLeaseState
{
    Unknown = 0,
    Active = 1,
    Expiring = 2,
    Expired = 3
}

public enum WorkflowRuntimeCommandRetryAction
{
    Unknown = 0,
    Continue = 1,
    Retry = 2,
    SkipCommand = 3,
    StopWorker = 4
}

public sealed record WorkflowRuntimeCommandLease(
    Guid CommandId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt,
    DateTimeOffset LastHeartbeatAt);

public sealed record WorkflowRuntimeCommandLeaseEvaluation(
    WorkflowRuntimeCommandLeaseState State,
    TimeSpan TimeRemaining,
    bool CanContinueLocalApply,
    bool CanReportToControl,
    bool ShouldHeartbeat);

public sealed record WorkflowRuntimeCommandRetryDecision(
    WorkflowRuntimeCommandRetryAction Action,
    TimeSpan Delay,
    string Reason);

public sealed class WorkflowRuntimeCommandLeasePolicy
{
    private readonly WorkflowArtifactRuntimeOptions _options;

    public WorkflowRuntimeCommandLeasePolicy(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        _options = options;
    }

    public WorkflowRuntimeCommandLease Create(WorkflowRuntimeCommandClaim claim)
    {
        if (string.IsNullOrWhiteSpace(claim.LeaseToken))
            throw new InvalidOperationException("Runtime command claim did not include a lease token.");
        if (!IsLeasedStatus(claim.Command.Status))
            throw new InvalidOperationException("Runtime command claim did not return a leased command.");
        if (!string.Equals(claim.Command.WorkerId, _options.WorkerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime command claim did not prove ownership by this worker.");
        if (claim.Command.LeaseExpiresAt is null)
            throw new InvalidOperationException("Runtime command claim did not include a lease expiration.");

        return new WorkflowRuntimeCommandLease(
            claim.Command.Id,
            claim.LeaseToken,
            claim.Command.LeaseExpiresAt.Value,
            claim.Command.HeartbeatAt ?? claim.Command.ClaimedAt ?? claim.Command.UpdatedAt);
    }

    public WorkflowRuntimeCommandLease Refresh(WorkflowRuntimeCommandLease lease, WorkflowRuntimeCommand command)
    {
        if (command.Id != lease.CommandId)
            throw new InvalidOperationException("Runtime command lease cannot be refreshed from a different command.");
        if (!IsLeasedStatus(command.Status))
            throw new InvalidOperationException("Runtime command lease cannot be refreshed from an unleased command.");
        if (!string.Equals(command.WorkerId, _options.WorkerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime command lease cannot be refreshed from a different worker.");

        return lease with
        {
            LeaseExpiresAt = command.LeaseExpiresAt ?? lease.LeaseExpiresAt,
            LastHeartbeatAt = command.HeartbeatAt ?? command.UpdatedAt
        };
    }

    public WorkflowRuntimeCommandLeaseEvaluation Evaluate(WorkflowRuntimeCommandLease lease, DateTimeOffset now)
    {
        var timeRemaining = lease.LeaseExpiresAt - now;
        if (timeRemaining <= TimeSpan.Zero)
            return new WorkflowRuntimeCommandLeaseEvaluation(WorkflowRuntimeCommandLeaseState.Expired, TimeSpan.Zero, false, false, false);

        var shouldHeartbeat = now - lease.LastHeartbeatAt >= _options.HeartbeatInterval;
        if (timeRemaining <= _options.LeaseSafetyMargin)
            return new WorkflowRuntimeCommandLeaseEvaluation(WorkflowRuntimeCommandLeaseState.Expiring, timeRemaining, false, true, shouldHeartbeat);

        return new WorkflowRuntimeCommandLeaseEvaluation(WorkflowRuntimeCommandLeaseState.Active, timeRemaining, true, true, shouldHeartbeat);
    }

    private static bool IsLeasedStatus(WorkflowRuntimeCommandStatus status) =>
        status is WorkflowRuntimeCommandStatus.Claimed or WorkflowRuntimeCommandStatus.Running;
}

public sealed class WorkflowRuntimeCommandRetryPolicy
{
    private readonly WorkflowArtifactRuntimeOptions _options;

    public WorkflowRuntimeCommandRetryPolicy(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        _options = options;
    }

    public WorkflowRuntimeCommandRetryDecision Decide(WorkflowRuntimeCommandClientStatus status, int retryAttempt)
    {
        if (retryAttempt < 0)
            throw new InvalidOperationException("Runtime command retry attempt must be zero or positive.");

        return status switch
        {
            WorkflowRuntimeCommandClientStatus.Succeeded =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.Continue, TimeSpan.Zero, "Runtime command request succeeded."),
            WorkflowRuntimeCommandClientStatus.Conflict =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.SkipCommand, TimeSpan.Zero, "Runtime command is no longer owned by this worker."),
            WorkflowRuntimeCommandClientStatus.NotFound =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.SkipCommand, TimeSpan.Zero, "Runtime command no longer exists."),
            WorkflowRuntimeCommandClientStatus.RetryableError when retryAttempt < _options.MaxRetryAttempts =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.Retry, DelayFor(retryAttempt), "Control returned a retryable runtime command error."),
            WorkflowRuntimeCommandClientStatus.RetryableError =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.SkipCommand, TimeSpan.Zero, "Retry attempts were exhausted for the runtime command."),
            WorkflowRuntimeCommandClientStatus.Unauthorized =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.StopWorker, TimeSpan.Zero, "Runtime command authorization failed."),
            WorkflowRuntimeCommandClientStatus.ValidationFailed =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.StopWorker, TimeSpan.Zero, "Control rejected the runtime command request."),
            _ =>
                new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.StopWorker, TimeSpan.Zero, "Runtime command response status is unknown.")
        };
    }

    private TimeSpan DelayFor(int retryAttempt)
    {
        var multiplier = Math.Pow(2, retryAttempt);
        var ticks = _options.RetryBaseDelay.Ticks * multiplier;
        if (ticks >= _options.RetryMaxDelay.Ticks)
            return _options.RetryMaxDelay;

        return TimeSpan.FromTicks(Math.Max(1, (long)ticks));
    }
}
