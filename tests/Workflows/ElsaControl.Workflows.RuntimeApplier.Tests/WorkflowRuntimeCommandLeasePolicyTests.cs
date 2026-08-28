using ElsaControl.Workflows.RuntimeApplier;

namespace ElsaControl.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowRuntimeCommandLeasePolicyTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CommandId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-29T10:00:00Z");

    private readonly WorkflowArtifactRuntimeOptions _options;
    private readonly WorkflowRuntimeCommandLeasePolicy _leasePolicy;
    private readonly WorkflowRuntimeCommandRetryPolicy _retryPolicy;

    public WorkflowRuntimeCommandLeasePolicyTests()
    {
        _options = new WorkflowArtifactRuntimeOptions
        {
            ControlEndpoint = new Uri("https://control.example.test"),
            WorkspaceId = WorkspaceId,
            EngineId = EngineId,
            WorkerId = "worker-a",
            ClaimLeaseDuration = TimeSpan.FromMinutes(5),
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            LeaseSafetyMargin = TimeSpan.FromSeconds(15),
            RetryBaseDelay = TimeSpan.FromSeconds(1),
            RetryMaxDelay = TimeSpan.FromSeconds(5),
            MaxRetryAttempts = 3
        };
        _leasePolicy = new WorkflowRuntimeCommandLeasePolicy(_options);
        _retryPolicy = new WorkflowRuntimeCommandRetryPolicy(_options);
    }

    [Fact]
    public void Creates_lease_from_claim_and_marks_heartbeat_due()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var evaluation = _leasePolicy.Evaluate(lease, Now.AddSeconds(31));

        Assert.Equal(CommandId, lease.CommandId);
        Assert.Equal("lease-1", lease.LeaseToken);
        Assert.Equal(WorkflowRuntimeCommandLeaseState.Active, evaluation.State);
        Assert.True(evaluation.CanContinueLocalApply);
        Assert.True(evaluation.CanReportToControl);
        Assert.True(evaluation.ShouldHeartbeat);
    }

    [Fact]
    public void Stops_local_apply_inside_safety_margin_before_expiration()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now.AddMinutes(4)), "lease-1"));

        var evaluation = _leasePolicy.Evaluate(lease, Now.AddMinutes(4).AddSeconds(50));

        Assert.Equal(WorkflowRuntimeCommandLeaseState.Expiring, evaluation.State);
        Assert.Equal(TimeSpan.FromSeconds(10), evaluation.TimeRemaining);
        Assert.False(evaluation.CanContinueLocalApply);
        Assert.True(evaluation.CanReportToControl);
    }

    [Fact]
    public void Rejects_control_reporting_after_lease_expiration()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var evaluation = _leasePolicy.Evaluate(lease, Now.AddMinutes(5));

        Assert.Equal(WorkflowRuntimeCommandLeaseState.Expired, evaluation.State);
        Assert.False(evaluation.CanContinueLocalApply);
        Assert.False(evaluation.CanReportToControl);
        Assert.False(evaluation.ShouldHeartbeat);
    }

    [Fact]
    public void Refreshes_lease_heartbeat_from_reported_command()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var refreshed = _leasePolicy.Refresh(lease, Command(Now.AddMinutes(5), heartbeatAt: Now.AddSeconds(40)));
        var evaluation = _leasePolicy.Evaluate(refreshed, Now.AddSeconds(45));

        Assert.Equal(Now.AddSeconds(40), refreshed.LastHeartbeatAt);
        Assert.False(evaluation.ShouldHeartbeat);
    }

    [Fact]
    public void Rejects_claim_without_provable_lease()
    {
        var act = () => _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(leaseExpiresAt: null, heartbeatAt: Now), "lease-1"));

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command claim did not include a lease expiration.", exception.Message);
    }

    [Fact]
    public void Rejects_claim_without_lease_token()
    {
        var act = () => _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), " "));

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command claim did not include a lease token.", exception.Message);
    }

    [Theory]
    [InlineData(WorkflowRuntimeCommandStatus.Pending, "worker-a", "Runtime command claim did not return a leased command.")]
    [InlineData(WorkflowRuntimeCommandStatus.Claimed, "worker-b", "Runtime command claim did not prove ownership by this worker.")]
    public void Rejects_claim_when_lease_ownership_is_not_proven(
        WorkflowRuntimeCommandStatus status,
        string workerId,
        string message)
    {
        var act = () => _leasePolicy.Create(new WorkflowRuntimeCommandClaim(
            Command(Now.AddMinutes(5), heartbeatAt: Now) with
            {
                Status = status,
                WorkerId = workerId
            },
            "lease-1"));

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void Rejects_refresh_when_lease_ownership_is_no_longer_proven()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var act = () => _leasePolicy.Refresh(
            lease,
            Command(Now.AddMinutes(5), heartbeatAt: Now.AddSeconds(40)) with
            {
                Status = WorkflowRuntimeCommandStatus.Completed
            });

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command lease cannot be refreshed from an unleased command.", exception.Message);
    }

    [Fact]
    public void Rejects_refresh_from_different_worker()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var act = () => _leasePolicy.Refresh(
            lease,
            Command(Now.AddMinutes(5), heartbeatAt: Now.AddSeconds(40)) with
            {
                WorkerId = "worker-b"
            });

        var exception = Assert.Throws<InvalidOperationException>(act);

        Assert.Equal("Runtime command lease cannot be refreshed from a different worker.", exception.Message);
    }

    [Fact]
    public void Retry_policy_uses_bounded_exponential_backoff()
    {
        Assert.Equal(
            new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.Retry, TimeSpan.FromSeconds(1), "Control returned a retryable runtime command error."),
            _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 0));
        Assert.Equal(TimeSpan.FromSeconds(2), _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 1).Delay);
        Assert.Equal(WorkflowRuntimeCommandRetryAction.SkipCommand, _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 3).Action);
    }

    [Theory]
    [InlineData(WorkflowRuntimeCommandClientStatus.Conflict)]
    [InlineData(WorkflowRuntimeCommandClientStatus.NotFound)]
    public void Retry_policy_skips_commands_that_cannot_be_owned(WorkflowRuntimeCommandClientStatus status)
    {
        var decision = _retryPolicy.Decide(status, retryAttempt: 0);

        Assert.Equal(WorkflowRuntimeCommandRetryAction.SkipCommand, decision.Action);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
    }

    [Theory]
    [InlineData(WorkflowRuntimeCommandClientStatus.Unauthorized)]
    [InlineData(WorkflowRuntimeCommandClientStatus.ValidationFailed)]
    [InlineData(WorkflowRuntimeCommandClientStatus.Unknown)]
    public void Retry_policy_stops_worker_on_non_recoverable_status(WorkflowRuntimeCommandClientStatus status)
    {
        Assert.Equal(WorkflowRuntimeCommandRetryAction.StopWorker, _retryPolicy.Decide(status, retryAttempt: 0).Action);
    }

    private static WorkflowRuntimeCommand Command(DateTimeOffset? leaseExpiresAt, DateTimeOffset? heartbeatAt) =>
        new(
            CommandId,
            WorkspaceId,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            EngineId,
            WorkflowRuntimeCommandAction.Deploy,
            WorkflowRuntimeCommandStatus.Claimed,
            null,
            null,
            "deploy-payment-retry",
            "worker-a",
            Now,
            leaseExpiresAt,
            heartbeatAt,
            1,
            null,
            null,
            null,
            null,
            [],
            Now,
            heartbeatAt ?? Now,
            null,
            null,
            null);
}
