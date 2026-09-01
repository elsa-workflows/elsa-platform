using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureElsaInstanceProviderTests
{
    private static readonly Guid TestInstanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("3.8", "3.8.0-preview.5413")]
    [InlineData("3.10", "3.10.4")]
    [InlineData("4.1", "4.1.0")]
    [InlineData("5.0", "5.0.0")]
    public async Task Submission_preserves_arbitrary_admitted_release_lines_and_stable_correlation(
        string releaseLine,
        string version)
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var plan = Translate(releaseLine, version);
        var service = new CapturingOperationService(CreateOperation(workspaceId, plan, operationId));
        var provider = new AzureElsaInstanceProvider(
            service,
            new CapturingOperationStore(),
            new AzureElsaInstanceProviderOptions { Enabled = true });
        var request = CreateSubmission(workspaceId, instanceId, operationId, plan);

        var first = await provider.SubmitAsync(request);
        var second = await provider.SubmitAsync(request);

        Assert.False(first.Replayed);
        Assert.False(second.Replayed);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal(2, service.Submissions.Count);
        Assert.Equal(service.Submissions[0].IdempotencyKey, service.Submissions[1].IdempotencyKey);
        Assert.Equal($"elsa-instance-operation:{operationId:D}", service.Submissions[0].IdempotencyKey);
        Assert.All(service.Submissions, submission =>
        {
            Assert.Equal(version, submission.Plan.ElsaVersion);
            Assert.Equal(releaseLine, submission.Plan.ReleaseLine);
        });
    }

    [Fact]
    public async Task Healthy_observation_projects_only_safe_provider_neutral_deployment_identity()
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var plan = Translate("5.0", "5.0.0");
        var operation = CreateOperation(workspaceId, plan, operationId) with
        {
            Status = AzureProviderOperationStatus.Succeeded,
            AttemptNumber = 2,
            Health = AzureProviderHealth.Healthy,
            Endpoint = "https://runtime.example.test/"
        };
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(operation),
            new CapturingOperationStore(operation),
            new AzureElsaInstanceProviderOptions { Enabled = true });

        var observation = await provider.ObserveAsync(new(
            workspaceId,
            instanceId,
            operationId,
            2,
            ElsaDesiredLifecycle.Running,
            null,
            null));

        Assert.Equal(ElsaInstanceProviderObservationKind.Confirmed, observation.Kind);
        Assert.Equal(ElsaObservedLifecycle.Ready, observation.ObservedLifecycle);
        Assert.Equal(ElsaInstanceProviderHealthGate.Passed, observation.HealthGate);
        Assert.Equal(operation.OperationIdentity, observation.CorrelationId);
        Assert.Equal(operation.OperationIdentity, observation.CurrentDeploymentReference?.DeploymentId);
        Assert.Equal("attempt-2", observation.CurrentDeploymentReference?.RevisionId);
        Assert.Equal("https://runtime.example.test", observation.CurrentDeploymentReference?.EndpointUri);
    }

    [Theory]
    [InlineData(AzureProviderOperationStatus.Succeeded, AzureProviderHealth.Degraded, ElsaObservedLifecycle.Ready, ElsaInstanceProviderHealthGate.Failed)]
    [InlineData(AzureProviderOperationStatus.Succeeded, AzureProviderHealth.Unreachable, ElsaObservedLifecycle.Ready, ElsaInstanceProviderHealthGate.Unknown)]
    [InlineData(AzureProviderOperationStatus.Succeeded, AzureProviderHealth.Failed, ElsaObservedLifecycle.Failed, ElsaInstanceProviderHealthGate.Failed)]
    [InlineData(AzureProviderOperationStatus.Failed, AzureProviderHealth.Failed, ElsaObservedLifecycle.Failed, ElsaInstanceProviderHealthGate.Failed)]
    [InlineData(AzureProviderOperationStatus.Cancelled, AzureProviderHealth.Unknown, ElsaObservedLifecycle.Failed, ElsaInstanceProviderHealthGate.Failed)]
    [InlineData(AzureProviderOperationStatus.Running, AzureProviderHealth.Unknown, ElsaObservedLifecycle.Provisioning, ElsaInstanceProviderHealthGate.Unknown)]
    [InlineData(AzureProviderOperationStatus.RecoveryRequired, AzureProviderHealth.Unknown, ElsaObservedLifecycle.Provisioning, ElsaInstanceProviderHealthGate.Unknown)]
    public async Task Provider_statuses_project_to_safe_observed_lifecycle_and_health(
        AzureProviderOperationStatus status,
        AzureProviderHealth health,
        ElsaObservedLifecycle expectedLifecycle,
        ElsaInstanceProviderHealthGate expectedHealth)
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var plan = Translate("5.0", "5.0.0");
        var operation = CreateOperation(workspaceId, plan, operationId) with { Status = status, Health = health };
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(operation),
            new CapturingOperationStore(operation),
            new AzureElsaInstanceProviderOptions { Enabled = true });

        var observation = await provider.ObserveAsync(new(
            workspaceId,
            instanceId,
            operationId,
            1,
            ElsaDesiredLifecycle.Running,
            null,
            null));

        Assert.Equal(ElsaInstanceProviderObservationKind.Confirmed, observation.Kind);
        Assert.Equal(expectedLifecycle, observation.ObservedLifecycle);
        Assert.Equal(expectedHealth, observation.HealthGate);
        if (status == AzureProviderOperationStatus.Succeeded && expectedLifecycle == ElsaObservedLifecycle.Ready && health != AzureProviderHealth.Unreachable)
            Assert.NotNull(observation.CurrentDeploymentReference);
        else
            Assert.Null(observation.CurrentDeploymentReference);
    }

    [Fact]
    public async Task Missing_provider_operation_is_unknown_and_does_not_claim_health()
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(null),
            new CapturingOperationStore(),
            new AzureElsaInstanceProviderOptions { Enabled = true });

        var observation = await provider.ObserveAsync(new(
            workspaceId,
            instanceId,
            operationId,
            1,
            ElsaDesiredLifecycle.Running,
            null,
            null));

        Assert.Equal(ElsaInstanceProviderObservationKind.Unknown, observation.Kind);
        Assert.Equal(ElsaObservedLifecycle.Unknown, observation.ObservedLifecycle);
        Assert.Equal(ElsaInstanceProviderHealthGate.Unknown, observation.HealthGate);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("target")]
    [InlineData("action")]
    [InlineData("idempotency")]
    [InlineData("scope")]
    public async Task Mismatched_provider_operation_identity_is_ambiguous_and_unknown(string mismatch)
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var plan = Translate("5.0", "5.0.0");
        var operation = CreateOperation(workspaceId, plan, operationId) with
        {
            WorkspaceId = mismatch == "workspace" ? Guid.NewGuid() : workspaceId,
            TargetKey = mismatch == "target" ? "different-target" : AzureElsaInstanceProvider.WorkloadName(TestInstanceId),
            Action = mismatch == "action" ? AzureProviderOperationAction.Delete : AzureProviderOperationAction.Reconcile,
            IdempotencyKey = mismatch == "idempotency" ? "elsa-instance-operation:different" : AzureElsaInstanceProvider.IdempotencyKey(operationId)
        };
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(operation),
            new CapturingOperationStore(operation),
            new AzureElsaInstanceProviderOptions
            {
                Enabled = true,
                ProviderScopeFingerprint = mismatch == "scope" ? new string('b', 64) : null
            });

        var observation = await provider.ObserveAsync(new(
            workspaceId,
            TestInstanceId,
            operationId,
            1,
            ElsaDesiredLifecycle.Running,
            null,
            null));

        Assert.Equal(ElsaInstanceProviderObservationKind.Ambiguous, observation.Kind);
        Assert.Equal(ElsaObservedLifecycle.Unknown, observation.ObservedLifecycle);
        Assert.Equal(ElsaInstanceProviderHealthGate.Unknown, observation.HealthGate);
        Assert.Equal("provider-operation-correlation-mismatch", observation.CorrelationId);
        Assert.Null(observation.CurrentDeploymentReference);
    }

    [Fact]
    public async Task Disabled_provider_fails_closed_before_submission()
    {
        var plan = Translate("5.0", "5.0.0");
        var service = new CapturingOperationService(CreateOperation(Guid.NewGuid(), plan, Guid.NewGuid()));
        var provider = new AzureElsaInstanceProvider(
            service,
            new CapturingOperationStore(),
            new AzureElsaInstanceProviderOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SubmitAsync(
            CreateSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plan)));
        Assert.Empty(service.Submissions);
    }

    private static AzureWorkloadPlan Translate(string releaseLine, string version) =>
        AzureWorkloadPlanTranslator.Translate(
            AzureWorkloadPlanTranslatorTests.CreatePlan(releaseLine, version),
            new("workload-a", "westeurope")).Plan!;

    private static ElsaInstanceProviderSubmission CreateSubmission(
        Guid workspaceId,
        Guid instanceId,
        Guid operationId,
        AzureWorkloadPlan plan) =>
        new(
            workspaceId,
            instanceId,
            operationId,
            1,
            ElsaDesiredLifecycle.Running,
            ToResolvedPlan(plan),
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            "westeurope");

    private static ResolvedElsaApplicationPlan ToResolvedPlan(AzureWorkloadPlan plan) =>
        AzureWorkloadPlanTranslatorTests.CreatePlan(plan.ReleaseLine, plan.ElsaVersion);

    private static AzureProviderOperation CreateOperation(
        Guid workspaceId,
        AzureWorkloadPlan plan,
        Guid operationId) =>
        new(
            operationId,
            workspaceId,
            AzureElsaInstanceProvider.WorkloadName(TestInstanceId),
            AzureProviderOperationAction.Reconcile,
            $"elsa-instance-operation:{operationId:D}",
            new string('a', 64),
            $"azure-operation-{operationId:N}",
            plan.Fingerprint,
            AzureElsaInstanceProviderOptions.DefaultTemplateFingerprint,
            plan.ElsaVersion,
            plan.ReleaseLine,
            plan.Topology,
            plan.Isolation,
            plan.Location,
            plan.ImageRepository,
            $"sha256:{plan.ImageDigest}",
            plan.ReleaseManifestDigest,
            plan.ReleaseManifestSignatureDigest,
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
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            plan.ReleaseManifestReference,
            plan.ReleaseManifestSignatureReference,
            plan.SecretReferences);

    private sealed class CapturingOperationService(AzureProviderOperation? operation) : IAzureProviderOperationService
    {
        public List<AzureProviderOperationSubmission> Submissions { get; } = [];

        public Task<AzureProviderOperation> SubmitAsync(
            Guid workspaceId,
            AzureProviderOperationSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            return Task.FromResult(operation!);
        }

        public Task<AzureProviderOperation> SubmitDeleteAsync(
            Guid workspaceId,
            AzureProviderOperationSubmission submission,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AzureProviderOperationStatusResponse?> GetStatusAsync(
            Guid workspaceId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperationStatusResponse?>(null);
    }

    private sealed class CapturingOperationStore(AzureProviderOperation? operation = null) : IAzureProviderOperationStore
    {
        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) => Task.FromResult(operation);
        public Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AzureProviderOperation>>([]);
        public Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, string? providerScopeFingerprint, CancellationToken cancellationToken = default) => Task.FromResult(operation);
        public Task<AzureProviderOperation?> MarkUnrestorableAsync(Guid workspaceId, Guid operationId, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
        public Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AzureProviderOperationTransition>>([]);
    }
}
