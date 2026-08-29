using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderExecutorTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Applies_the_checked_in_lifecycle_and_persists_safe_checkpoints()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { HostileDiagnostics = true, RunnerMessage = "provider returned an internal payload" };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, result.Operation.Status);
        Assert.Equal(AzureProviderOperationPhase.TrafficPromoted, result.Operation.Phase);
        Assert.Equal(AzureProviderHealth.Healthy, result.Operation.Health);
        Assert.Equal("https://workload.example.test", result.Operation.Endpoint);
        Assert.Equal(
            [
                AzureProviderRunnerStep.Foundation,
                AzureProviderRunnerStep.AcrPull,
                AzureProviderRunnerStep.SeedSecrets,
                AzureProviderRunnerStep.SqlBootstrap,
                AzureProviderRunnerStep.Workload,
                AzureProviderRunnerStep.Health,
                AzureProviderRunnerStep.Promotion
            ],
            runner.Steps);
        Assert.All(result.Operation.Diagnostics, diagnostic => Assert.Equal(diagnostic.Code, diagnostic.Message));
        Assert.DoesNotContain(result.Operation.Diagnostics, diagnostic => diagnostic.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("internal payload", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reapplying_a_succeeded_operation_is_an_explicit_no_op()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner();
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));
        var request = CreateRequest();
        var plan = CreatePlan();

        var first = await executor.ApplyAsync(request, plan);
        var second = await executor.ApplyAsync(request, plan);

        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, first.Outcome);
        Assert.Equal(AzureProviderExecutionOutcome.NoOp, second.Outcome);
        Assert.Equal(first.Operation.Id, second.Operation.Id);
        Assert.Equal(7, runner.Steps.Count);
    }

    [Fact]
    public async Task An_interrupted_remote_step_is_recovery_required_and_can_resume_idempotently()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { FailFoundationOnce = true };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));
        var request = CreateRequest();
        var plan = CreatePlan();

        var interrupted = await executor.ApplyAsync(request, plan);
        var resumed = await executor.ApplyAsync(request, plan);

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, interrupted.Outcome);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, interrupted.Operation.Status);
        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, resumed.Outcome);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, resumed.Operation.Status);
        Assert.Equal(8, runner.Steps.Count);
        Assert.Equal(AzureProviderRunnerStep.Foundation, runner.Steps[1]);
    }

    [Fact]
    public async Task Failed_or_uncertain_promotion_restores_the_prior_stable_revision()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { PromotionOutcome = AzureProviderRunnerOutcome.Uncertain };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, result.Operation.Status);
        Assert.Equal(
            [
                AzureProviderRunnerStep.Foundation,
                AzureProviderRunnerStep.AcrPull,
                AzureProviderRunnerStep.SeedSecrets,
                AzureProviderRunnerStep.SqlBootstrap,
                AzureProviderRunnerStep.Workload,
                AzureProviderRunnerStep.Health,
                AzureProviderRunnerStep.Promotion,
                AzureProviderRunnerStep.RestoreStableTraffic
            ],
            runner.Steps);
        Assert.Equal("stable-revision", runner.Commands[^1].StableTrafficRevisionName);
        Assert.Equal(AzureProviderOperationPhase.HealthVerified, result.Operation.Phase);
    }

    [Fact]
    public async Task Unhealthy_candidate_never_reaches_promotion()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { Health = AzureProviderHealth.Degraded };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.Failed, result.Operation.Status);
        Assert.DoesNotContain(AzureProviderRunnerStep.Promotion, runner.Steps);
    }

    [Fact]
    public async Task Promotion_failure_without_a_stable_revision_stays_in_recovery()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { PromotionOutcome = AzureProviderRunnerOutcome.Failed, StableTrafficRevisionName = null };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.DoesNotContain(AzureProviderRunnerStep.RestoreStableTraffic, runner.Steps);
    }

    [Fact]
    public async Task Hostile_runner_message_is_not_returned_or_persisted()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { RunnerMessage = "password=do-not-persist\r\nraw provider response" };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Operation);

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.DoesNotContain("do-not-persist", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-persist", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execution_requires_both_verified_manifest_digests()
    {
        var store = new FakeOperationStore();
        var executor = new AzureProviderExecutor(store, new RecordingRunner(), new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() => executor.ApplyAsync(CreateRequest(), CreatePlan() with { ReleaseManifestDigest = null! }));
    }

    [Fact]
    public async Task Renews_the_durable_lease_while_a_remote_step_is_running()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { Delay = TimeSpan.FromMilliseconds(30) };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, result.Outcome);
        Assert.True(store.HeartbeatCount > 0);
    }

    [Fact]
    public async Task Persists_recovery_using_the_latest_version_after_a_heartbeated_failure()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { Delay = TimeSpan.FromMilliseconds(30), ThrowAfterDelay = true };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, result.Operation.Status);
        Assert.True(store.HeartbeatCount > 0);
    }

    [Theory]
    [InlineData(AzureProviderOperationStatus.Failed, AzureProviderExecutionOutcome.Failed)]
    [InlineData(AzureProviderOperationStatus.Cancelled, AzureProviderExecutionOutcome.Failed)]
    [InlineData(AzureProviderOperationStatus.RecoveryRequired, AzureProviderExecutionOutcome.RecoveryRequired)]
    public async Task Claim_race_reports_the_observed_operation_state(
        AzureProviderOperationStatus observedStatus,
        AzureProviderExecutionOutcome expectedOutcome)
    {
        var store = new FakeOperationStore { RejectClaimWithStatus = observedStatus };
        var executor = new AzureProviderExecutor(store, new RecordingRunner(), new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(observedStatus, result.Operation.Status);
    }

    [Fact]
    public async Task Checkpoint_race_reports_recovery_required_instead_of_in_progress()
    {
        var store = new FakeOperationStore { RejectCheckpointWithStatus = AzureProviderOperationStatus.RecoveryRequired };
        var executor = new AzureProviderExecutor(store, new RecordingRunner(), new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, result.Operation.Status);
    }

    [Fact]
    public async Task Lease_loss_signals_cancellation_to_the_still_running_runner()
    {
        var store = new FakeOperationStore { LoseLeaseOnHeartbeat = true };
        var runner = new RecordingRunner { WaitForCancellationStep = AzureProviderRunnerStep.Foundation };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        await runner.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(AzureProviderExecutionOutcome.InProgress, result.Outcome);
        Assert.True(runner.CancellationObserved.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cancellation_after_a_heartbeat_persists_recovery_using_the_latest_version()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { Delay = TimeSpan.FromMilliseconds(100) };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ApplyAsync(CreateRequest(), CreatePlan(), cancellation.Token);
        await runner.Started.Task;
        for (var attempt = 0; store.HeartbeatCount == 0 && attempt < 100; attempt++)
            await Task.Delay(2);
        Assert.True(store.HeartbeatCount > 0);
        cancellation.Cancel();
        var result = await execution;

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, result.Operation.Status);
        Assert.True(result.Operation.Version > 2);
    }

    [Fact]
    public async Task Unsafe_runner_endpoint_fails_closed_without_leaking_runner_text()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner
        {
            EndpointOverride = "https://user:password@workload.example.test/secret"
        };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Operation);

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.DoesNotContain("password", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsafe_runner_resource_reference_fails_closed_without_leaking_runner_text()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner
        {
            ResourcesOverride = new(ResourceGroupName: "proof-rg", WorkloadResourceId: "secret://provider-response")
        };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Operation);

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.DoesNotContain("provider-response", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-response", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_requires_the_runner_to_prove_exact_owned_resource_absence()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { CleanupResources = new(ResourceGroupName: "still-present") };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));

        var result = await executor.DeleteAsync(CreateRequest(AzureProviderOperationAction.Delete), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(AzureProviderOperationStatus.Failed, result.Operation.Status);
        Assert.Equal(AzureProviderRunnerStep.Cleanup, runner.Steps[^1]);
        Assert.Equal(AzureProviderOperationPhase.CleanupSubmitted, result.Operation.Phase);
    }

    [Fact]
    public async Task Delete_happy_path_is_idempotent()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { CleanupResources = new() };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));
        var request = CreateRequest(AzureProviderOperationAction.Delete);
        var plan = CreatePlan();

        var first = await executor.DeleteAsync(request, plan);
        var second = await executor.DeleteAsync(request, plan);

        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, first.Outcome);
        Assert.Equal(AzureProviderExecutionOutcome.NoOp, second.Outcome);
        Assert.Single(runner.Steps, AzureProviderRunnerStep.Cleanup);
    }

    [Fact]
    public async Task Interrupted_cleanup_enters_recovery_and_resumes()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { CleanupResources = new(), FailCleanupOnce = true };
        var executor = new AzureProviderExecutor(store, runner, new StaticTimeProvider(Now), TimeSpan.FromMinutes(5));
        var request = CreateRequest(AzureProviderOperationAction.Delete);
        var plan = CreatePlan();

        var interrupted = await executor.DeleteAsync(request, plan);
        var resumed = await executor.DeleteAsync(request, plan);

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, interrupted.Outcome);
        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, resumed.Outcome);
        Assert.Equal([AzureProviderRunnerStep.Cleanup, AzureProviderRunnerStep.Cleanup], runner.Steps);
    }

    [Fact]
    public async Task Cleanup_renews_the_durable_lease_while_running()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner { CleanupResources = new(), Delay = TimeSpan.FromMilliseconds(30) };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));

        var result = await executor.DeleteAsync(CreateRequest(AzureProviderOperationAction.Delete), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.Succeeded, result.Outcome);
        Assert.True(store.HeartbeatCount > 0);
    }

    [Fact]
    public async Task Stable_traffic_restoration_renews_the_lease_while_running()
    {
        var store = new FakeOperationStore();
        var runner = new RecordingRunner
        {
            PromotionOutcome = AzureProviderRunnerOutcome.Uncertain,
            Delay = TimeSpan.FromMilliseconds(30),
            DelayOnlyStep = AzureProviderRunnerStep.RestoreStableTraffic
        };
        var executor = new AzureProviderExecutor(
            store,
            runner,
            new StaticTimeProvider(Now),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(5));

        var result = await executor.ApplyAsync(CreateRequest(), CreatePlan());

        Assert.Equal(AzureProviderExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.True(store.HeartbeatCount > 0);
        Assert.Equal(AzureProviderRunnerStep.RestoreStableTraffic, runner.Steps[^1]);
    }

    private static AzureProviderOperationRequest CreateRequest(AzureProviderOperationAction action = AzureProviderOperationAction.Reconcile) => new(
        WorkspaceId,
        "workload-a",
        action,
        "request-1",
        new('a', 64),
        new('b', 64),
        "3.8.0-preview.5413",
        "3.8",
        "combined",
        "Dedicated",
        "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined",
        "sha256:" + new string('c', 64),
        "sha256:" + new string('d', 64),
        "sha256:" + new string('e', 64));

    private static AzureWorkloadPlan CreatePlan() => new(
        "workload-a",
        "westeurope",
        "3.8.0-preview.5413",
        "3.8",
        "combined",
        "Dedicated",
        "valenceruntimeimages.azurecr.io/runtime-combined",
        new('c', 64),
        "oci://release-manifest.example/manifest",
        "sha256:" + new string('d', 64),
        "oci://release-manifest.example/signature",
        "sha256:" + new string('e', 64),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["database:connectionstring"] = "secret://database"
        },
        new('a', 64));

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRunner : IAzureProviderRunner
    {
        public List<AzureProviderRunnerStep> Steps { get; } = [];
        public List<AzureProviderRunnerCommand> Commands { get; } = [];
        public bool FailFoundationOnce { get; init; }
        public bool FailCleanupOnce { get; init; }
        public AzureProviderRunnerOutcome PromotionOutcome { get; init; } = AzureProviderRunnerOutcome.Completed;
        public AzureProviderHealth Health { get; init; } = AzureProviderHealth.Healthy;
        public AzureProviderResourceReferences CleanupResources { get; init; } = new(ResourceGroupName: "proof-rg");
        public string? StableTrafficRevisionName { get; init; } = "stable-revision";
        public AzureProviderRunnerStep? DelayOnlyStep { get; init; }
        public string? EndpointOverride { get; init; }
        public AzureProviderResourceReferences? ResourcesOverride { get; init; }
        public TimeSpan Delay { get; init; }
        public bool HostileDiagnostics { get; init; }
        public string RunnerMessage { get; init; } = "Azure lifecycle step completed.";
        public bool ThrowAfterDelay { get; init; }
        public AzureProviderRunnerStep? WaitForCancellationStep { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken = default)
        {
            Steps.Add(command.Step);
            Commands.Add(command);
            Started.TrySetResult();
            if (command.Step == AzureProviderRunnerStep.Foundation && FailFoundationOnce && Steps.Count(x => x == AzureProviderRunnerStep.Foundation) == 1)
                throw new InvalidOperationException("remote result cannot be classified safely");
            if (command.Step == AzureProviderRunnerStep.Cleanup && FailCleanupOnce && Steps.Count(x => x == AzureProviderRunnerStep.Cleanup) == 1)
                throw new InvalidOperationException("remote cleanup result cannot be classified safely");

            if (WaitForCancellationStep == command.Step)
                return WaitForCancellationAsync(cancellationToken);

            if (Delay > TimeSpan.Zero && (!DelayOnlyStep.HasValue || DelayOnlyStep == command.Step))
                return DelayedResultAsync(command);
            return Task.FromResult(CreateResult(command));
        }

        private async Task<AzureProviderRunnerResult> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation wait completed unexpectedly.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }

        private async Task<AzureProviderRunnerResult> DelayedResultAsync(AzureProviderRunnerCommand command)
        {
            await Task.Delay(Delay);
            if (ThrowAfterDelay)
                throw new InvalidOperationException("remote result was not confirmed");
            return CreateResult(command);
        }

        private AzureProviderRunnerResult CreateResult(AzureProviderRunnerCommand command)
        {
            if (command.Step == AzureProviderRunnerStep.Promotion)
                return Result(PromotionOutcome, AzureProviderOperationPhase.TrafficPromoted);
            if (command.Step == AzureProviderRunnerStep.RestoreStableTraffic)
                return Result(AzureProviderRunnerOutcome.Completed, AzureProviderOperationPhase.HealthVerified);
            if (command.Step == AzureProviderRunnerStep.Cleanup)
                return Result(AzureProviderRunnerOutcome.Completed, AzureProviderOperationPhase.CleanupVerified, CleanupResources);
            if (command.Step == AzureProviderRunnerStep.Health)
                return Result(AzureProviderRunnerOutcome.Completed, AzureProviderOperationPhase.HealthVerified, health: Health);
            return Result(
                AzureProviderRunnerOutcome.Completed,
                command.Step is AzureProviderRunnerStep.Foundation or AzureProviderRunnerStep.AcrPull or AzureProviderRunnerStep.SeedSecrets
                    ? AzureProviderOperationPhase.FoundationSubmitted
                    : command.Step == AzureProviderRunnerStep.SqlBootstrap
                        ? AzureProviderOperationPhase.FoundationReady
                        : AzureProviderOperationPhase.WorkloadReady);
        }

        private AzureProviderRunnerResult Result(
            AzureProviderRunnerOutcome outcome,
            AzureProviderOperationPhase phase,
            AzureProviderResourceReferences? resources = null,
            AzureProviderHealth health = AzureProviderHealth.Unknown) => new(
            outcome,
            phase,
            resources ?? ResourcesOverride ?? new(ResourceGroupName: "proof-rg", FoundationDeploymentId: "foundation-1", WorkloadDeploymentId: "workload-1", WorkloadResourceId: "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/app", WorkloadRevisionName: "app--candidate", StableTrafficRevisionName: StableTrafficRevisionName),
            health == AzureProviderHealth.Unknown && phase is AzureProviderOperationPhase.HealthVerified or AzureProviderOperationPhase.TrafficPromoted ? AzureProviderHealth.Healthy : health,
            EndpointOverride ?? (phase is AzureProviderOperationPhase.HealthVerified or AzureProviderOperationPhase.TrafficPromoted ? "https://workload.example.test" : null),
            HostileDiagnostics
                ? [new AzureProviderDiagnostic("azure.provider.detail", "password=do-not-persist\r\nraw response")]
                : [],
            "azure.step.completed",
            RunnerMessage);
    }

    private sealed class FakeOperationStore : IAzureProviderOperationStore
    {
        private AzureProviderOperation? _operation;
        private readonly List<AzureProviderOperationTransition> _transitions = [];
        public int HeartbeatCount { get; private set; }
        public AzureProviderOperationStatus? RejectClaimWithStatus { get; init; }
        public AzureProviderOperationStatus? RejectCheckpointWithStatus { get; init; }
        public bool LoseLeaseOnHeartbeat { get; init; }

        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var normalized = AzureProviderOperationValidation.Normalize(request);
            if (_operation is not null)
            {
                if (_operation.RequestHash != AzureProviderOperationValidation.ComputeRequestHash(normalized))
                    throw new InvalidOperationException("The idempotency key is already bound to a different request.");
                return Task.FromResult(_operation);
            }

            _operation = new(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                normalized.WorkspaceId,
                normalized.TargetKey,
                normalized.Action,
                normalized.IdempotencyKey,
                AzureProviderOperationValidation.ComputeRequestHash(normalized),
                AzureProviderOperationValidation.ComputeOperationIdentity(normalized),
                normalized.PlanFingerprint,
                normalized.TemplateFingerprint,
                normalized.ElsaVersion,
                normalized.ReleaseLine,
                normalized.Topology,
                normalized.Isolation,
                normalized.Location,
                normalized.ImageRepository,
                normalized.ImageDigest,
                normalized.ReleaseManifestDigest,
                normalized.ReleaseManifestSignatureDigest,
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
                now,
                now,
                null);
            return Task.FromResult(_operation);
        }

        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_operation is { WorkspaceId: var id, Id: var operationIdValue } && id == workspaceId && operationIdValue == operationId ? _operation : null);

        public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => ClaimCore(workerId, leaseToken, leaseDuration, now, expectedVersion, false);

        public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => ClaimCore(workerId, leaseToken, leaseDuration, now, expectedVersion, true);

        private Task<AzureProviderOperation?> ClaimCore(string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion, bool recovery)
        {
            if (_operation is not null && RejectClaimWithStatus is { } observedStatus)
            {
                _operation = _operation with { Status = observedStatus, Version = _operation.Version + 1, UpdatedAt = now };
                return Task.FromResult<AzureProviderOperation?>(null);
            }
            if (_operation is null || expectedVersion.HasValue && _operation.Version != expectedVersion.Value || _operation.Status != (recovery ? AzureProviderOperationStatus.RecoveryRequired : AzureProviderOperationStatus.Accepted))
                return Task.FromResult<AzureProviderOperation?>(null);
            _operation = _operation with
            {
                Status = AzureProviderOperationStatus.Running,
                AttemptNumber = _operation.AttemptNumber + 1,
                Version = _operation.Version + 1,
                WorkerId = workerId,
                LeaseExpiresAt = now.Add(leaseDuration),
                HeartbeatAt = now,
                UpdatedAt = now
            };
            return Task.FromResult<AzureProviderOperation?>(_operation);
        }

        public Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            HeartbeatCount++;
            if (LoseLeaseOnHeartbeat)
                return Task.FromResult<AzureProviderOperation?>(null);
            if (_operation is null || expectedVersion.HasValue && _operation.Version != expectedVersion.Value)
                return Task.FromResult<AzureProviderOperation?>(null);
            _operation = _operation with { Version = _operation.Version + 1, UpdatedAt = now, HeartbeatAt = now, LeaseExpiresAt = now.Add(leaseDuration) };
            return Task.FromResult<AzureProviderOperation?>(_operation);
        }

        public Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            if (_operation is not null && RejectCheckpointWithStatus is { } observedStatus)
            {
                _operation = _operation with { Status = observedStatus, Version = _operation.Version + 1, UpdatedAt = now };
                return Task.FromResult<AzureProviderOperation?>(null);
            }
            if (_operation is null || _operation.Status != AzureProviderOperationStatus.Running || expectedVersion.HasValue && _operation.Version != expectedVersion.Value)
                return Task.FromResult<AzureProviderOperation?>(null);
            _operation = _operation with
            {
                Phase = checkpoint.Phase,
                CheckpointSequence = _operation.CheckpointSequence + 1,
                Version = _operation.Version + 1,
                Resources = checkpoint.Resources,
                Endpoint = checkpoint.Endpoint,
                Health = checkpoint.Health,
                Diagnostics = checkpoint.Diagnostics,
                UpdatedAt = now
            };
            return Task.FromResult<AzureProviderOperation?>(_operation);
        }

        public Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, string message, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            if (_operation is null || _operation.Status != AzureProviderOperationStatus.Running || expectedVersion.HasValue && _operation.Version != expectedVersion.Value)
                return Task.FromResult<AzureProviderOperation?>(null);
            _operation = _operation with { Status = status, Version = _operation.Version + 1, UpdatedAt = now, CompletedAt = status == AzureProviderOperationStatus.RecoveryRequired ? null : now, WorkerId = null, LeaseExpiresAt = null };
            _transitions.Add(new(_operation.Id, _operation.Id, _transitions.Count + 1, status, _operation.Phase, code, code, now));
            return Task.FromResult<AzureProviderOperation?>(_operation);
        }

        public Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AzureProviderOperationTransition>>(_transitions);
    }
}
