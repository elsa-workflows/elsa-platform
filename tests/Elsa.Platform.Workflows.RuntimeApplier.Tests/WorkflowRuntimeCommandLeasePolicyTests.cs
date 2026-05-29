using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;

namespace Elsa.Platform.Workflows.RuntimeApplier.Tests;

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
            PlatformEndpoint = new Uri("https://platform.example.test"),
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

        lease.CommandId.Should().Be(CommandId);
        lease.LeaseToken.Should().Be("lease-1");
        evaluation.State.Should().Be(WorkflowRuntimeCommandLeaseState.Active);
        evaluation.CanContinueLocalApply.Should().BeTrue();
        evaluation.CanReportToPlatform.Should().BeTrue();
        evaluation.ShouldHeartbeat.Should().BeTrue();
    }

    [Fact]
    public void Stops_local_apply_inside_safety_margin_before_expiration()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now.AddMinutes(4)), "lease-1"));

        var evaluation = _leasePolicy.Evaluate(lease, Now.AddMinutes(4).AddSeconds(50));

        evaluation.State.Should().Be(WorkflowRuntimeCommandLeaseState.Expiring);
        evaluation.TimeRemaining.Should().Be(TimeSpan.FromSeconds(10));
        evaluation.CanContinueLocalApply.Should().BeFalse();
        evaluation.CanReportToPlatform.Should().BeTrue();
    }

    [Fact]
    public void Rejects_platform_reporting_after_lease_expiration()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var evaluation = _leasePolicy.Evaluate(lease, Now.AddMinutes(5));

        evaluation.State.Should().Be(WorkflowRuntimeCommandLeaseState.Expired);
        evaluation.CanContinueLocalApply.Should().BeFalse();
        evaluation.CanReportToPlatform.Should().BeFalse();
        evaluation.ShouldHeartbeat.Should().BeFalse();
    }

    [Fact]
    public void Refreshes_lease_heartbeat_from_reported_command()
    {
        var lease = _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), "lease-1"));

        var refreshed = _leasePolicy.Refresh(lease, Command(Now.AddMinutes(5), heartbeatAt: Now.AddSeconds(40)));
        var evaluation = _leasePolicy.Evaluate(refreshed, Now.AddSeconds(45));

        refreshed.LastHeartbeatAt.Should().Be(Now.AddSeconds(40));
        evaluation.ShouldHeartbeat.Should().BeFalse();
    }

    [Fact]
    public void Rejects_claim_without_provable_lease()
    {
        var act = () => _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(leaseExpiresAt: null, heartbeatAt: Now), "lease-1"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime command claim did not include a lease expiration.");
    }

    [Fact]
    public void Rejects_claim_without_lease_token()
    {
        var act = () => _leasePolicy.Create(new WorkflowRuntimeCommandClaim(Command(Now.AddMinutes(5), heartbeatAt: Now), " "));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime command claim did not include a lease token.");
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

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(message);
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

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime command lease cannot be refreshed from an unleased command.");
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

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime command lease cannot be refreshed from a different worker.");
    }

    [Fact]
    public void Retry_policy_uses_bounded_exponential_backoff()
    {
        _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 0)
            .Should().Be(new WorkflowRuntimeCommandRetryDecision(WorkflowRuntimeCommandRetryAction.Retry, TimeSpan.FromSeconds(1), "Platform returned a retryable runtime command error."));
        _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 1).Delay.Should().Be(TimeSpan.FromSeconds(2));
        _retryPolicy.Decide(WorkflowRuntimeCommandClientStatus.RetryableError, retryAttempt: 3).Action.Should().Be(WorkflowRuntimeCommandRetryAction.SkipCommand);
    }

    [Theory]
    [InlineData(WorkflowRuntimeCommandClientStatus.Conflict)]
    [InlineData(WorkflowRuntimeCommandClientStatus.NotFound)]
    public void Retry_policy_skips_commands_that_cannot_be_owned(WorkflowRuntimeCommandClientStatus status)
    {
        var decision = _retryPolicy.Decide(status, retryAttempt: 0);

        decision.Action.Should().Be(WorkflowRuntimeCommandRetryAction.SkipCommand);
        decision.Delay.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(WorkflowRuntimeCommandClientStatus.Unauthorized)]
    [InlineData(WorkflowRuntimeCommandClientStatus.ValidationFailed)]
    [InlineData(WorkflowRuntimeCommandClientStatus.Unknown)]
    public void Retry_policy_stops_worker_on_non_recoverable_status(WorkflowRuntimeCommandClientStatus status)
    {
        _retryPolicy.Decide(status, retryAttempt: 0).Action.Should().Be(WorkflowRuntimeCommandRetryAction.StopWorker);
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
