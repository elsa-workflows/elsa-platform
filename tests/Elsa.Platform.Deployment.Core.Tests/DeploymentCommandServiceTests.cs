using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class DeploymentCommandServiceTests
{
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.Parse("2026-05-28T10:00:00Z"));
    private readonly RecordingCommandStore _store = new();
    private readonly DeploymentCommandService _service;
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly Guid _commandId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private readonly Guid _engineId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    public DeploymentCommandServiceTests()
    {
        _service = new DeploymentCommandService(_store, _clock);
        _store.Commands[_commandId] = Command(DeploymentCommandStatus.Pending);
    }

    [Fact]
    public async Task Poll_clamps_limit_and_returns_pending_commands()
    {
        await _service.PollPendingCommandsAsync(_workspaceId, _engineId, 1000);

        _store.LastPollLimit.Should().Be(100);
    }

    [Fact]
    public async Task Claim_assigns_lease_and_rejects_duplicate_claims()
    {
        var claim = await _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));
        var duplicate = () => _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-2", TimeSpan.FromMinutes(5)));

        claim.LeaseToken.Should().NotBeNullOrWhiteSpace();
        claim.Command.Status.Should().Be(DeploymentCommandStatus.Claimed);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Progress_redacts_sensitive_diagnostics_before_persistence()
    {
        var claim = await _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));

        var command = await _service.ProgressAsync(
            _workspaceId,
            _commandId,
            new DeploymentCommandProgressRequest(claim.LeaseToken, "applying", 50, "Using bearer token secret-value"));

        command.ProgressMessage.Should().Be("Using [redacted] [redacted] [redacted]-value");
    }

    [Fact]
    public async Task Heartbeat_updates_worker_ownership()
    {
        var claim = await _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));

        var command = await _service.HeartbeatAsync(
            _workspaceId,
            _commandId,
            new DeploymentCommandHeartbeatRequest(claim.LeaseToken, "runtime-1"));

        command.HeartbeatAt.Should().Be(_clock.GetUtcNow());
        command.WorkerId.Should().Be("runtime-1");
    }

    [Fact]
    public async Task Complete_is_idempotent_after_final_state()
    {
        var claim = await _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));
        var request = new CompleteDeploymentCommandRequest(claim.LeaseToken, null, "workflow:42", []);

        var first = await _service.CompleteAsync(_workspaceId, _commandId, request);
        var second = await _service.CompleteAsync(_workspaceId, _commandId, request with { LeaseToken = "duplicate-delivery" });

        first.Status.Should().Be(DeploymentCommandStatus.Completed);
        second.Status.Should().Be(DeploymentCommandStatus.Completed);
    }

    [Fact]
    public async Task Fail_and_reject_sanitize_diagnostics()
    {
        var failClaim = await _service.ClaimCommandAsync(
            _workspaceId,
            _commandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));

        var failed = await _service.FailAsync(
            _workspaceId,
            _commandId,
            new FailDeploymentCommandRequest(
                failClaim.LeaseToken,
                [new DeploymentCommandDiagnostic("apply-failed", DeploymentCommandDiagnosticSeverity.Error, "password secret leaked")]));

        var rejectCommandId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        _store.Commands[rejectCommandId] = Command(DeploymentCommandStatus.Pending) with { Id = rejectCommandId };
        var rejectClaim = await _service.ClaimCommandAsync(
            _workspaceId,
            rejectCommandId,
            new ClaimDeploymentCommandRequest(_engineId, "runtime-1", TimeSpan.FromMinutes(5)));
        var rejected = await _service.RejectAsync(
            _workspaceId,
            rejectCommandId,
            new RejectDeploymentCommandRequest(
                rejectClaim.LeaseToken,
                [new DeploymentCommandDiagnostic("unsupported", DeploymentCommandDiagnosticSeverity.Warning, "private key unavailable")]));

        failed.Status.Should().Be(DeploymentCommandStatus.Failed);
        failed.Diagnostics.Single().Message.Should().Be("[redacted] [redacted] leaked");
        rejected.Status.Should().Be(DeploymentCommandStatus.Rejected);
        rejected.Diagnostics.Single().Message.Should().Be("[redacted] unavailable");
    }

    [Fact]
    public async Task Recover_stale_commands_delegates_cutoff_to_store()
    {
        _store.RecoveredCount = 2;

        var recovered = await _service.RecoverStaleCommandsAsync(TimeSpan.FromMinutes(10));

        recovered.Should().Be(2);
        _store.LastStaleAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Webhook_notification_payload_contains_safe_command_hint_only()
    {
        var notification = await _service.CreateWebhookNotificationAsync(_workspaceId, _engineId, _commandId);

        notification.SafePayloadJson.Should().Contain(_workspaceId.ToString("D"));
        notification.SafePayloadJson.Should().Contain(_engineId.ToString("D"));
        notification.SafePayloadJson.Should().Contain(_commandId.ToString("D"));
        notification.SafePayloadJson.Should().NotContain("lease");
        notification.SafePayloadJson.Should().NotContain("secret");
    }

    [Fact]
    public async Task Webhook_dispatch_sends_safe_payload_and_marks_notification_sent()
    {
        var notificationId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var sender = new RecordingWebhookSender(DeploymentWebhookDispatchResult.Sent());
        _store.WebhookTargets.Add(WebhookTarget(notificationId, "https://runtime.example.test/elsa"));
        var dispatcher = new DeploymentWebhookDispatchService(
            _store,
            sender,
            new DeploymentWebhookDispatchOptions { Enabled = true, NotificationPath = "platform/webhooks/commands" },
            _clock);

        var processed = await dispatcher.DispatchPendingAsync();

        processed.Should().Be(1);
        sender.Requests.Should().ContainSingle();
        sender.Requests.Single().Endpoint.Should().Be(new Uri("https://runtime.example.test/elsa/platform/webhooks/commands"));
        sender.Requests.Single().Target.SafePayloadJson.Should().Contain("command-available");
        _store.WebhookStatuses[notificationId].Should().Be(WebhookNotificationStatus.Sent);
    }

    [Fact]
    public async Task Webhook_dispatch_skips_invalid_runtime_endpoint_without_sending()
    {
        var notificationId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var sender = new RecordingWebhookSender(DeploymentWebhookDispatchResult.Sent());
        _store.WebhookTargets.Add(WebhookTarget(notificationId, "ftp://runtime.example.test"));
        var dispatcher = new DeploymentWebhookDispatchService(
            _store,
            sender,
            new DeploymentWebhookDispatchOptions { Enabled = true },
            _clock);

        var processed = await dispatcher.DispatchPendingAsync();

        processed.Should().Be(1);
        sender.Requests.Should().BeEmpty();
        _store.WebhookStatuses[notificationId].Should().Be(WebhookNotificationStatus.Skipped);
    }

    [Fact]
    public async Task Webhook_dispatch_marks_failed_when_sender_fails()
    {
        var notificationId = Guid.Parse("50000000-0000-0000-0000-000000000003");
        var sender = new RecordingWebhookSender(DeploymentWebhookDispatchResult.Failed("Runtime unavailable."));
        _store.WebhookTargets.Add(WebhookTarget(notificationId, "https://runtime.example.test"));
        var dispatcher = new DeploymentWebhookDispatchService(
            _store,
            sender,
            new DeploymentWebhookDispatchOptions { Enabled = true },
            _clock);

        await dispatcher.DispatchPendingAsync();

        _store.WebhookStatuses[notificationId].Should().Be(WebhookNotificationStatus.Failed);
    }

    private DeploymentWebhookNotificationDispatchTarget WebhookTarget(Guid notificationId, string? engineBaseUrl) =>
        new(
            notificationId,
            _workspaceId,
            _engineId,
            _commandId,
            $$"""{"workspaceId":"{{_workspaceId:D}}","engineId":"{{_engineId:D}}","commandHint":"{{_commandId:D}}","reason":"command-available"}""",
            engineBaseUrl,
            _clock.GetUtcNow());

    private DeploymentCommand Command(DeploymentCommandStatus status, string? leaseToken = null) =>
        new(
            _commandId,
            _workspaceId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            _engineId,
            DeploymentCommandAction.Deploy,
            status,
            null,
            new DeploymentCommandRevisionReference(Guid.NewGuid()),
            "deployment-command:test",
            status == DeploymentCommandStatus.Pending ? null : "runtime-1",
            leaseToken,
            status == DeploymentCommandStatus.Pending ? null : _clock.GetUtcNow(),
            status == DeploymentCommandStatus.Pending ? null : _clock.GetUtcNow().AddMinutes(5),
            status == DeploymentCommandStatus.Pending ? null : _clock.GetUtcNow(),
            status == DeploymentCommandStatus.Pending ? 0 : 1,
            null,
            null,
            null,
            null,
            [],
            _clock.GetUtcNow(),
            _clock.GetUtcNow(),
            _clock.GetUtcNow(),
            null,
            status is DeploymentCommandStatus.Completed ? _clock.GetUtcNow() : null);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingCommandStore : IWorkspaceDeploymentCommandStore
    {
        public Dictionary<Guid, DeploymentCommand> Commands { get; } = [];
        public List<DeploymentWebhookNotificationDispatchTarget> WebhookTargets { get; } = [];
        public Dictionary<Guid, WebhookNotificationStatus> WebhookStatuses { get; } = [];
        public int LastPollLimit { get; private set; }
        public int RecoveredCount { get; set; }
        public TimeSpan? LastStaleAfter { get; private set; }

        public Task<IReadOnlyList<DeploymentCommand>> PollPendingCommandsAsync(Guid workspaceId, Guid engineId, int limit, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            LastPollLimit = limit;
            return Task.FromResult<IReadOnlyList<DeploymentCommand>>(
                Commands.Values.Where(x => x.WorkspaceId == workspaceId && x.EngineId == engineId && x.Status == DeploymentCommandStatus.Pending).Take(limit).ToList());
        }

        public Task<DeploymentCommand> ClaimCommandAsync(Guid workspaceId, Guid commandId, ClaimDeploymentCommandRequest request, string leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId];
            if (command.Status != DeploymentCommandStatus.Pending)
                throw new InvalidOperationException("Command is already leased.");

            command = command with
            {
                Status = DeploymentCommandStatus.Claimed,
                WorkerId = request.WorkerId,
                LeaseToken = leaseToken,
                ClaimedAt = now,
                HeartbeatAt = now,
                LeaseExpiresAt = now.Add(request.LeaseDuration),
                AttemptNumber = command.AttemptNumber + 1,
                UpdatedAt = now
            };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }

        public Task<DeploymentCommand> RecordCommandProgressAsync(Guid workspaceId, Guid commandId, DeploymentCommandProgressRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId] with
            {
                Status = DeploymentCommandStatus.Running,
                PercentComplete = request.PercentComplete,
                ProgressMessage = request.Message,
                UpdatedAt = now
            };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }

        public Task<DeploymentCommand> CompleteCommandAsync(Guid workspaceId, Guid commandId, CompleteDeploymentCommandRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId];
            if (command.Status == DeploymentCommandStatus.Completed)
                return Task.FromResult(command);

            command = command with { Status = DeploymentCommandStatus.Completed, RuntimeReference = request.RuntimeReference, CompletedAt = now, UpdatedAt = now };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }

        public Task<int> MarkStaleCommandsRecoveryRequiredAsync(DateTimeOffset now, TimeSpan staleAfter, CancellationToken cancellationToken = default)
        {
            LastStaleAfter = staleAfter;
            return Task.FromResult(RecoveredCount);
        }

        public Task<DeploymentCommand> CreateCommandAsync(Guid workspaceId, CreateDeploymentCommandRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeploymentCommand?> GetCommandAsync(Guid workspaceId, Guid commandId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeploymentCommand> HeartbeatCommandAsync(Guid workspaceId, Guid commandId, DeploymentCommandHeartbeatRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId] with { WorkerId = request.WorkerId, HeartbeatAt = now, UpdatedAt = now };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }

        public Task<DeploymentCommand> FailCommandAsync(Guid workspaceId, Guid commandId, FailDeploymentCommandRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId] with { Status = DeploymentCommandStatus.Failed, Diagnostics = request.Diagnostics, CompletedAt = now, UpdatedAt = now };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }

        public Task<DeploymentCommand> RejectCommandAsync(Guid workspaceId, Guid commandId, RejectDeploymentCommandRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var command = Commands[commandId] with { Status = DeploymentCommandStatus.Rejected, Diagnostics = request.Diagnostics, CompletedAt = now, UpdatedAt = now };
            Commands[commandId] = command;
            return Task.FromResult(command);
        }
        public Task<DeploymentCommandWebhookNotification> CreateWebhookNotificationAsync(Guid workspaceId, Guid engineId, Guid commandId, string safePayloadJson, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeploymentCommandWebhookNotification(Guid.NewGuid(), workspaceId, engineId, commandId, WebhookNotificationStatus.Pending, safePayloadJson, now, null));

        public Task<IReadOnlyList<DeploymentWebhookNotificationDispatchTarget>> ListPendingWebhookNotificationTargetsAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeploymentWebhookNotificationDispatchTarget>>(WebhookTargets.Take(limit).ToList());

        public Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationSentAsync(Guid workspaceId, Guid notificationId, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
        {
            WebhookStatuses[notificationId] = WebhookNotificationStatus.Sent;
            return Task.FromResult(Notification(notificationId, WebhookNotificationStatus.Sent, sentAt));
        }

        public Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationFailedAsync(Guid workspaceId, Guid notificationId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            WebhookStatuses[notificationId] = WebhookNotificationStatus.Failed;
            return Task.FromResult(Notification(notificationId, WebhookNotificationStatus.Failed, null));
        }

        public Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationSkippedAsync(Guid workspaceId, Guid notificationId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            WebhookStatuses[notificationId] = WebhookNotificationStatus.Skipped;
            return Task.FromResult(Notification(notificationId, WebhookNotificationStatus.Skipped, null));
        }

        private DeploymentCommandWebhookNotification Notification(Guid notificationId, WebhookNotificationStatus status, DateTimeOffset? sentAt) =>
            new(notificationId, Guid.Empty, Guid.Empty, Guid.Empty, status, "{}", DateTimeOffset.UtcNow, sentAt);
    }

    private sealed class RecordingWebhookSender(DeploymentWebhookDispatchResult result) : IDeploymentWebhookSender
    {
        public List<DeploymentWebhookDispatchRequest> Requests { get; } = [];

        public Task<DeploymentWebhookDispatchResult> SendAsync(DeploymentWebhookDispatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}
