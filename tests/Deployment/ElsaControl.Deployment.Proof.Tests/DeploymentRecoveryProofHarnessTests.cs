using System.Globalization;
using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class DeploymentRecoveryProofHarnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Happy_path_restores_to_a_new_target_and_only_returns_cutover_eligibility()
    {
        var provider = new RecoveryFakeProvider();
        var report = await RunAsync(provider);

        Assert.True(report.Passed, report.Failure?.Message);
        Assert.True(report.CutoverEligible);
        Assert.Equal("source-instance", report.RecoveryPoint.SourceInstanceId);
        Assert.Equal("target-instance", report.Target?.InstanceId);
        Assert.Equal(TimeSpan.FromHours(1), report.RpoAge);
        Assert.Equal(
            [
                DeploymentRecoveryStage.RecoveryPointValidation,
                DeploymentRecoveryStage.CreateIsolatedTarget,
                DeploymentRecoveryStage.RestoreRelationalState,
                DeploymentRecoveryStage.RebindExternalSecrets,
                DeploymentRecoveryStage.ValidateImmutableInputs,
                DeploymentRecoveryStage.TargetHealth,
                DeploymentRecoveryStage.WorkflowValidation,
                DeploymentRecoveryStage.CutoverEligibility,
                DeploymentRecoveryStage.Cleanup
            ],
            report.Stages.Select(stage => stage.Stage));
        Assert.All(report.Stages, stage => Assert.Equal(DeploymentRecoveryStageStatus.Passed, stage.Status));
        Assert.Equal(1, provider.CleanupCalls);
        Assert.Equal("target-instance", provider.CleanedTargetIds.Single());
        Assert.DoesNotContain("cutover", provider.Calls.Where(call => call.Contains("mutat", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("\"secretReferenceKeyCount\": \"3\"", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stale_recovery_point_is_rejected_before_target_creation()
    {
        var provider = new RecoveryFakeProvider();
        var point = RecoveryPoint(capturedAt: Now - TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        var report = await new DeploymentRecoveryProofHarness(timeProvider: new ManualTimeProvider(Now))
            .RunAsync(point, provider);

        Assert.False(report.Passed);
        Assert.Equal("recovery.point.stale", report.Failure?.Code);
        Assert.Equal(0, provider.CreateCalls);
        Assert.Equal(0, provider.CleanupCalls);
        Assert.False(report.CutoverEligible);
    }

    [Fact]
    public async Task Invalid_digest_or_embedded_reference_digest_is_rejected()
    {
        var provider = new RecoveryFakeProvider();
        var point = RecoveryPoint(
            resolvedPlanReference: "oci://plans/plan@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var report = await RunAsync(provider, point);

        Assert.False(report.Passed);
        Assert.Equal("recovery.point.digestOrReferenceInvalid", report.Failure?.Code);
        Assert.Equal(0, provider.CreateCalls);
        Assert.False(report.CutoverEligible);
    }

    [Fact]
    public async Task Reusing_source_identity_is_rejected_without_offering_source_cleanup()
    {
        var provider = new RecoveryFakeProvider(targetIdentityReuse: true);
        var report = await RunAsync(provider);

        Assert.False(report.Passed);
        Assert.Equal("recovery.target.identityReuse", report.Failure?.Code);
        Assert.Equal(0, provider.CleanupCalls);
        Assert.False(report.CutoverEligible);
    }

    [Fact]
    public async Task Invalid_target_identity_is_rejected_and_redacted_from_serialized_evidence()
    {
        const string invalidTargetIdentity = "token=do-not-leak";
        var provider = new RecoveryFakeProvider(targetIdentity: invalidTargetIdentity);

        var report = await RunAsync(provider);
        var json = report.ToJson();

        Assert.False(report.Passed);
        Assert.Equal("recovery.target.invalid", report.Failure?.Code);
        Assert.Equal(0, provider.CleanupCalls);
        Assert.DoesNotContain(invalidTargetIdentity, json, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-leak", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecoveryFailure.SecretRebind)]
    [InlineData(RecoveryFailure.Restore)]
    [InlineData(RecoveryFailure.Immutable)]
    [InlineData(RecoveryFailure.Health)]
    [InlineData(RecoveryFailure.Workflow)]
    [InlineData(RecoveryFailure.Cutover)]
    public async Task Every_gate_failure_stops_later_gates_and_still_cleans_the_new_target(RecoveryFailure failure)
    {
        var provider = new RecoveryFakeProvider(failure: failure);
        var report = await RunAsync(provider);

        Assert.False(report.Passed);
        Assert.NotNull(report.Failure);
        Assert.Equal(1, provider.CleanupCalls);
        Assert.Equal("target-instance", provider.CleanedTargetIds.Single());
        Assert.False(report.CutoverEligible);
        if (failure == RecoveryFailure.Cutover)
            Assert.Contains("cutover", provider.Calls);
        else
            Assert.DoesNotContain("cutover", provider.Calls);

        var failingIndex = report.Stages.ToList().IndexOf(report.Failure!);
        Assert.All(report.Stages.Skip(failingIndex + 1).Where(stage => stage.Stage != DeploymentRecoveryStage.Cleanup), stage =>
            Assert.Equal(DeploymentRecoveryStageStatus.Skipped, stage.Status));
    }

    [Fact]
    public async Task Secret_rebind_requires_an_exact_key_set_without_accepting_values()
    {
        var provider = new RecoveryFakeProvider(secretRebindMismatch: true);
        var report = await RunAsync(provider);

        Assert.False(report.Passed);
        Assert.Equal("recovery.secrets.mismatch", report.Failure?.Code);
        Assert.Equal(1, provider.CleanupCalls);
        Assert.DoesNotContain("do-not-leak", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rto_breach_prevents_cutover_eligibility_and_cleans_the_target()
    {
        var clock = new ManualTimeProvider(Now);
        var provider = new RecoveryFakeProvider(advanceDuringWorkflow: TimeSpan.FromHours(4) + TimeSpan.FromSeconds(1), clock: clock);
        var report = await new DeploymentRecoveryProofHarness(timeProvider: clock)
            .RunAsync(RecoveryPoint(), provider);

        Assert.False(report.Passed);
        Assert.Equal("recovery.rto.breached", report.Failure?.Code);
        Assert.False(report.CutoverEligible);
        Assert.Equal(0, provider.CutoverCalls);
        Assert.Equal(1, provider.CleanupCalls);
        Assert.True(report.Rto > TimeSpan.FromHours(4));
    }

    [Fact]
    public async Task Successful_cleanup_time_is_not_counted_as_restore_rto()
    {
        var clock = new ManualTimeProvider(Now);
        var provider = new RecoveryFakeProvider(
            advanceDuringWorkflow: TimeSpan.FromHours(3),
            advanceDuringCleanup: TimeSpan.FromHours(2),
            clock: clock);

        var report = await new DeploymentRecoveryProofHarness(timeProvider: clock)
            .RunAsync(RecoveryPoint(), provider);

        Assert.True(report.Passed, report.Failure?.Message);
        Assert.Equal(TimeSpan.FromHours(3), report.Rto);
        Assert.True(report.CutoverEligible);
        Assert.Equal(1, provider.CleanupCalls);
    }

    [Fact]
    public async Task Cancellation_after_target_creation_still_attempts_bounded_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new RecoveryFakeProvider(cancelDuringRestore: cancellation);
        var report = await new DeploymentRecoveryProofHarness(timeProvider: new ManualTimeProvider(Now))
            .RunAsync(RecoveryPoint(), provider, cancellation.Token);

        Assert.False(report.Passed);
        Assert.Equal("recovery.restore.cancelled", report.Failure?.Code);
        Assert.Equal(1, provider.CleanupCalls);
        Assert.False(report.CutoverEligible);
    }

    [Fact]
    public async Task Cleanup_timeout_is_bounded_and_never_changes_the_source_identity()
    {
        var provider = new RecoveryFakeProvider(hangCleanup: true);
        var report = await new DeploymentRecoveryProofHarness(
                timeProvider: new ManualTimeProvider(Now),
                cleanupTimeout: TimeSpan.FromMilliseconds(20))
            .RunAsync(RecoveryPoint(), provider);

        Assert.False(report.Passed);
        Assert.Equal("recovery.cleanup.cancelled", report.Failure?.Code);
        Assert.Equal(["target-instance"], provider.CleanedTargetIds);
        Assert.DoesNotContain("source-instance", provider.CleanedTargetIds);
        Assert.False(report.CutoverEligible);
    }

    [Fact]
    public async Task Evidence_is_value_free_when_a_provider_throws_with_secret_and_resource_details()
    {
        var provider = new RecoveryFakeProvider(throwingMessage: "token=do-not-leak provider-resource-id");
        var report = await RunAsync(provider);
        var json = report.ToJson();

        Assert.DoesNotContain("do-not-leak", json, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-resource-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery.restore.failed", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://user:password@registry.example/artifact")]
    [InlineData("https://registry.example/artifact?token=secret")]
    [InlineData("https://registry.example/artifact#secret")]
    [InlineData("https://registry.example/artifact\n")]
    public async Task Unsafe_references_fail_before_any_provider_operation(string reference)
    {
        var provider = new RecoveryFakeProvider();
        var point = RecoveryPoint(resolvedPlanReference: reference);

        var report = await RunAsync(provider, point);

        Assert.False(report.Passed);
        Assert.Equal(0, provider.CreateCalls);
        Assert.False(report.CutoverEligible);
        Assert.DoesNotContain(reference, report.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", report.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<DeploymentRecoveryProofReport> RunAsync(
        RecoveryFakeProvider provider,
        DeploymentRecoveryPoint? point = null) =>
        new DeploymentRecoveryProofHarness(timeProvider: new ManualTimeProvider(Now))
            .RunAsync(point ?? RecoveryPoint(), provider);

    private static DeploymentRecoveryPoint RecoveryPoint(
        DateTimeOffset? capturedAt = null,
        string? resolvedPlanReference = null) =>
        new(
            "organization-proof",
            "workspace-proof",
            "source-instance",
            "recovery-point-20260901",
            capturedAt ?? Now - TimeSpan.FromHours(1),
            (capturedAt ?? Now - TimeSpan.FromHours(1)) - TimeSpan.FromMinutes(2),
            capturedAt ?? Now - TimeSpan.FromHours(1),
            "Ready",
            Digest('0'),
            "desired-revision-42",
            Digest('1'),
            resolvedPlanReference ?? "oci://plans/plan-42",
            Digest('2'),
            [new DeploymentRecoveryArtifact("oci://artifacts/workflow-42", Digest('3'))],
            "snapshot://relational/20260901",
            Digest('4'),
            ["sql-connection", "identity-signing-key", "admin-password"]);

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    public enum RecoveryFailure
    {
        SecretRebind,
        Restore,
        Immutable,
        Health,
        Workflow,
        Cutover
    }

    private sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan duration) => _current += duration;
    }

    private sealed class RecoveryFakeProvider(
        RecoveryFailure? failure = null,
        bool targetIdentityReuse = false,
        string? targetIdentity = null,
        bool secretRebindMismatch = false,
        TimeSpan? advanceDuringWorkflow = null,
        TimeSpan? advanceDuringCleanup = null,
        ManualTimeProvider? clock = null,
        CancellationTokenSource? cancelDuringRestore = null,
        bool hangCleanup = false,
        string? throwingMessage = null) : IDeploymentRecoveryProvider
    {
        private readonly RecoveryFailure? _failure = failure;
        private readonly bool _targetIdentityReuse = targetIdentityReuse;
        private readonly string? _targetIdentity = targetIdentity;
        private readonly bool _secretRebindMismatch = secretRebindMismatch;
        private readonly TimeSpan _advanceDuringWorkflow = advanceDuringWorkflow ?? TimeSpan.Zero;
        private readonly TimeSpan _advanceDuringCleanup = advanceDuringCleanup ?? TimeSpan.Zero;
        private readonly ManualTimeProvider? _clock = clock;
        private readonly CancellationTokenSource? _cancelDuringRestore = cancelDuringRestore;
        private readonly bool _hangCleanup = hangCleanup;
        private readonly string? _throwingMessage = throwingMessage;

        public List<string> Calls { get; } = [];

        public List<string> CleanedTargetIds { get; } = [];

        public int CreateCalls { get; private set; }

        public int CleanupCalls { get; private set; }

        public int CutoverCalls { get; private set; }

        public Task<DeploymentRecoveryTarget> CreateIsolatedTargetAsync(DeploymentRecoveryPoint recoveryPoint, CancellationToken cancellationToken = default)
        {
            Calls.Add("target");
            CreateCalls++;
            ThrowIf(DeploymentRecoveryStage.CreateIsolatedTarget);
            return Task.FromResult(new DeploymentRecoveryTarget(
                _targetIdentityReuse ? recoveryPoint.SourceInstanceId : _targetIdentity ?? "target-instance"));
        }

        public Task<DeploymentRecoveryRestoredState> RestoreRelationalStateAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add("restore");
            if (_cancelDuringRestore is not null)
            {
                _cancelDuringRestore.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            ThrowIf(DeploymentRecoveryStage.RestoreRelationalState);
            var source = _throwingMessage is null ? recoveryPoint.SourceInstanceId : "source-instance";
            return Task.FromResult(new DeploymentRecoveryRestoredState(
                target.InstanceId,
                source,
                recoveryPoint.RecoveryPointId,
                recoveryPoint.DesiredRevisionId,
                recoveryPoint.DesiredRevisionHash,
                recoveryPoint.ResolvedPlanReference,
                recoveryPoint.ResolvedPlanDigest,
                recoveryPoint.Artifacts,
                recoveryPoint.ProviderSnapshotReference,
                recoveryPoint.ProviderSnapshotDigest));
        }

        public Task<DeploymentRecoverySecretRebind> RebindExternalSecretsAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add("secrets");
            ThrowIf(DeploymentRecoveryStage.RebindExternalSecrets);
            var keys = _secretRebindMismatch ? ["sql-connection"] : recoveryPoint.RequiredSecretReferenceKeys.ToArray();
            return Task.FromResult(new DeploymentRecoverySecretRebind(keys));
        }

        public Task<DeploymentRecoveryValidation> ValidateImmutableInputsAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, DeploymentRecoveryRestoredState restoredState, DeploymentRecoverySecretRebind secretRebind, CancellationToken cancellationToken = default)
        {
            Calls.Add("immutable");
            ThrowIf(DeploymentRecoveryStage.ValidateImmutableInputs);
            return Task.FromResult(new DeploymentRecoveryValidation(_failure != RecoveryFailure.Immutable));
        }

        public Task<DeploymentRecoveryHealth> ValidateTargetHealthAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add("health");
            ThrowIf(DeploymentRecoveryStage.TargetHealth);
            return Task.FromResult(new DeploymentRecoveryHealth(_failure != RecoveryFailure.Health, "healthy"));
        }

        public Task<DeploymentRecoveryWorkflow> ValidateWorkflowAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, DeploymentRecoveryHealth health, CancellationToken cancellationToken = default)
        {
            Calls.Add("workflow");
            ThrowIf(DeploymentRecoveryStage.WorkflowValidation);
            _clock?.Advance(_advanceDuringWorkflow);
            return Task.FromResult(new DeploymentRecoveryWorkflow(_failure != RecoveryFailure.Workflow, "completed"));
        }

        public Task<DeploymentRecoveryCutoverEligibility> EvaluateCutoverEligibilityAsync(DeploymentRecoveryPoint recoveryPoint, DeploymentRecoveryTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add("cutover");
            CutoverCalls++;
            ThrowIf(DeploymentRecoveryStage.CutoverEligibility);
            return Task.FromResult(new DeploymentRecoveryCutoverEligibility(_failure != RecoveryFailure.Cutover));
        }

        public async Task<DeploymentRecoveryCleanup> CleanupAsync(DeploymentRecoveryTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add("cleanup");
            CleanupCalls++;
            CleanedTargetIds.Add(target.InstanceId);
            if (_hangCleanup)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            _clock?.Advance(_advanceDuringCleanup);
            return new DeploymentRecoveryCleanup(true);
        }

        private void ThrowIf(DeploymentRecoveryStage stage)
        {
            if ((_throwingMessage is not null && stage == DeploymentRecoveryStage.RestoreRelationalState) ||
                stage switch
                {
                    DeploymentRecoveryStage.RebindExternalSecrets => _failure == RecoveryFailure.SecretRebind,
                    DeploymentRecoveryStage.RestoreRelationalState => _failure == RecoveryFailure.Restore,
                    _ => false
                })
                throw new DeploymentRecoveryStageException(stage, $"recovery.{stage.ToString().ToLowerInvariant()}.failed", _throwingMessage ?? "Injected failure.");
        }
    }
}
