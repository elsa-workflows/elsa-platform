using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureElsaInstanceProviderTests
{
    private static readonly Guid TestInstanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestOrganizationId = Guid.Parse("44444444-4444-4444-4444-444444444444");

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
        var service = new CapturingOperationService(CreateOperation(workspaceId, plan, operationId) with { AttemptNumber = 17 });
        var provider = new AzureElsaInstanceProvider(
            service,
            new CapturingOperationStore(),
            new InMemoryAssignmentStore(),
            options: EnabledOptions());
        var request = CreateSubmission(workspaceId, instanceId, operationId, plan);

        var first = await provider.SubmitAsync(request);
        var second = await provider.SubmitAsync(request);

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", first.PlacementAssignmentId);
        Assert.Equal(first.PlacementAssignmentId, second.PlacementAssignmentId);
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
            new InMemoryAssignmentStore(),
            options: EnabledOptions());

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
    [InlineData(AzureProviderHealth.Healthy, null)]
    [InlineData(AzureProviderHealth.Healthy, "https://runtime.example.test/api")]
    [InlineData(AzureProviderHealth.Degraded, null)]
    [InlineData(AzureProviderHealth.Degraded, "https://runtime.example.test/?token=secret")]
    public async Task Succeeded_healthy_or_degraded_operation_with_invalid_endpoint_is_ambiguous_and_value_free(
        AzureProviderHealth health,
        string? endpoint)
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var plan = Translate("5.0", "5.0.0");
        var operation = CreateOperation(workspaceId, plan, operationId) with
        {
            Status = AzureProviderOperationStatus.Succeeded,
            Health = health,
            Endpoint = endpoint
        };
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(operation),
            new CapturingOperationStore(operation),
            new InMemoryAssignmentStore(),
            options: EnabledOptions());

        var request = new ElsaInstanceProviderReconciliationRequest(
            workspaceId,
            TestInstanceId,
            operationId,
            1,
            ElsaDesiredLifecycle.Running,
            null,
            null);
        var first = await provider.ObserveAsync(request);
        var second = await provider.ObserveAsync(request);

        Assert.Equal(ElsaInstanceProviderObservationKind.Ambiguous, first.Kind);
        Assert.Equal(ElsaObservedLifecycle.Unknown, first.ObservedLifecycle);
        Assert.Equal(ElsaInstanceProviderHealthGate.Unknown, first.HealthGate);
        Assert.Equal("provider-operation-endpoint-invalid", first.CorrelationId);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.DoesNotContain("secret", first.CorrelationId, StringComparison.OrdinalIgnoreCase);
        Assert.Null(first.CurrentDeploymentReference);
        Assert.False(first.HasCurrentDeploymentProjection);
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
        var operation = CreateOperation(workspaceId, plan, operationId) with
        {
            Status = status,
            Health = health,
            Endpoint = status == AzureProviderOperationStatus.Succeeded &&
                       health is AzureProviderHealth.Healthy or AzureProviderHealth.Degraded
                ? "https://runtime.example.test/"
                : null
        };
        var provider = new AzureElsaInstanceProvider(
            new CapturingOperationService(operation),
            new CapturingOperationStore(operation),
            new InMemoryAssignmentStore(),
            options: EnabledOptions());

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
            new InMemoryAssignmentStore(),
            options: EnabledOptions());

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
            new InMemoryAssignmentStore(),
            options:
            new AzureElsaInstanceProviderOptions
            {
                Enabled = true,
                TemplateFingerprint = new string('b', 64),
                ProviderScopeFingerprint = mismatch == "scope" ? new string('b', 64) : new string('a', 64),
                SubscriptionId = "11111111-1111-1111-1111-111111111111",
                ResourceGroupNamePrefix = "rg-elsa"
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
            new InMemoryAssignmentStore(),
            options: new AzureElsaInstanceProviderOptions());

        var exception = await Assert.ThrowsAsync<ElsaInstanceProviderSubmissionException>(() => provider.SubmitAsync(
            CreateSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plan)));
        Assert.Equal(ElsaInstanceProviderSubmissionFailureKind.Rejected, exception.Kind);
        Assert.Empty(service.Submissions);
    }

    [Fact]
    public async Task Lifecycle_submission_without_an_organization_binding_is_rejected_before_durable_submission()
    {
        var plan = Translate("5.0", "5.0.0");
        var service = new CapturingOperationService(CreateOperation(Guid.NewGuid(), plan, Guid.NewGuid()));
        var provider = new AzureElsaInstanceProvider(service, new CapturingOperationStore(), new InMemoryAssignmentStore(), options: EnabledOptions());

        var exception = await Assert.ThrowsAsync<ElsaInstanceProviderSubmissionException>(() => provider.SubmitAsync(
            CreateSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plan) with { OrganizationId = null }));

        Assert.Equal(ElsaInstanceProviderSubmissionFailureKind.Rejected, exception.Kind);
        Assert.Empty(service.Submissions);
    }

    [Fact]
    public async Task Unsafe_extension_plan_is_rejected_before_durable_provider_submission()
    {
        var workspaceId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var translated = Translate("5.0", "5.0.0");
        var service = new CapturingOperationService(CreateOperation(workspaceId, translated, operationId));
        var provider = new AzureElsaInstanceProvider(service, new CapturingOperationStore(), new InMemoryAssignmentStore(), options: EnabledOptions());
        var request = CreateSubmission(workspaceId, Guid.NewGuid(), operationId, translated);
        var resolvedPlan = request.Plan;
        request = request with
        {
            Plan = resolvedPlan with
            {
                Packages =
                [
                    resolvedPlan.Packages[0] with
                    {
                        PackageId = "Customer.SecretPackage",
                        ExtensionClass = ResolvedExtensionClass.ArbitraryCustomer
                    }
                ]
            }
        };

        var exception = await Assert.ThrowsAsync<ElsaInstanceProviderSubmissionException>(() => provider.SubmitAsync(request));

        Assert.Equal(ElsaInstanceProviderSubmissionFailureKind.Rejected, exception.Kind);
        Assert.Empty(service.Submissions);
        Assert.DoesNotContain("Customer.SecretPackage", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durable_submission_failure_is_classified_as_outcome_unknown()
    {
        var plan = Translate("5.0", "5.0.0");
        var provider = new AzureElsaInstanceProvider(
            new ThrowingOperationService(),
            new CapturingOperationStore(),
            new InMemoryAssignmentStore(),
            options: EnabledOptions());

        var exception = await Assert.ThrowsAsync<ElsaInstanceProviderSubmissionException>(() => provider.SubmitAsync(
            CreateSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plan)));

        Assert.Equal(ElsaInstanceProviderSubmissionFailureKind.OutcomeUnknown, exception.Kind);
    }

    [Fact]
    public void Enabled_provider_requires_a_valid_scope_fingerprint()
    {
        Assert.Throws<ArgumentException>(() => new AzureElsaInstanceProviderOptions { Enabled = true }.Validate());
        Assert.Throws<ArgumentException>(() => new AzureElsaInstanceProviderOptions
        {
            Enabled = true,
            ProviderScopeFingerprint = "not-a-fingerprint"
        }.Validate());

        EnabledOptions().Validate();
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Cleanup_submits_or_reobserves_delete_and_confirms_only_verified_absence(bool alreadyDeleted, bool verified, bool retainedResource)
    {
        var workspaceId = Guid.NewGuid();
        var lifecycleOperationId = Guid.NewGuid();
        var plan = Translate("5.0", "5.0.0");
        plan = plan with { WorkloadName = AzureElsaInstanceProvider.WorkloadName(TestInstanceId) };
        var reconcile = CreateOperation(workspaceId, plan, Guid.NewGuid()) with
        {
            OrganizationId = TestOrganizationId,
            InstanceId = TestInstanceId,
            LifecycleAction = ElsaInstanceOperationAction.Reconcile,
            SqlWorkflowPackageVersion = plan.SqlWorkflowPackageVersion,
            SqlQuartzPackageVersion = plan.SqlQuartzPackageVersion
        };
        reconcile = reconcile with
        {
            RequestHash = AzureProviderOperationValidation.ComputeRequestHash(
                AzureProviderOperationService.CreateOperationRequest(reconcile)),
            OperationIdentity = AzureProviderOperationValidation.ComputeOperationIdentity(
                AzureProviderOperationService.CreateOperationRequest(reconcile))
        };
        Assert.NotNull(AzureProviderOperationService.TryRestorePlan(reconcile));
        var delete = reconcile with
        {
            Id = Guid.NewGuid(),
            Action = AzureProviderOperationAction.Delete,
            IdempotencyKey = AzureElsaInstanceProvider.IdempotencyKey(lifecycleOperationId) + ":delete",
            LifecycleAction = ElsaInstanceOperationAction.Delete,
            Status = verified ? AzureProviderOperationStatus.Succeeded : AzureProviderOperationStatus.Running,
            Phase = AzureProviderOperationPhase.CleanupVerified,
            Resources = new(),
            Endpoint = null
        };
        var assignmentStore = new InMemoryAssignmentStore
        {
            State = alreadyDeleted ? AzureProviderAssignmentState.Deleted : AzureProviderAssignmentState.Reserved,
            LastOperationId = alreadyDeleted ? delete.Id : null,
            Resources = retainedResource ? new(WorkloadResourceId: "/subscriptions/retained/resourceGroups/retained/providers/Microsoft.App/containerApps/retained") : new()
        };
        var assignment = await assignmentStore.CreateOrGetAsync(new(
            workspaceId,
            TestOrganizationId,
            TestInstanceId,
            new string('a', 64),
            "11111111-1111-1111-1111-111111111111",
            "rg-elsa",
            AzureElsaInstanceProvider.WorkloadName(TestInstanceId),
            "westeurope"),
            DateTimeOffset.UtcNow);
        reconcile = reconcile with { ProviderAssignmentId = assignment.Id };
        reconcile = reconcile with
        {
            RequestHash = AzureProviderOperationValidation.ComputeRequestHash(
                AzureProviderOperationService.CreateOperationRequest(reconcile)),
            OperationIdentity = AzureProviderOperationValidation.ComputeOperationIdentity(
                AzureProviderOperationService.CreateOperationRequest(reconcile))
        };
        delete = delete with { ProviderAssignmentId = assignment.Id };
        var service = new CapturingOperationService(reconcile) { DeleteOperation = delete };
        var provider = new AzureElsaInstanceProvider(service, new CapturingOperationStore(reconcile, delete), assignmentStore, options: EnabledOptions());

        var result = await provider.CleanupAsync(new(
            workspaceId, TestInstanceId, lifecycleOperationId, 3, null,
            new ElsaPlacementAssignmentReference(assignment.Id.ToString("D")), null));

        Assert.Equal(retainedResource ? "deletion.provider-evidence-unavailable" : verified ? "deletion.provider-confirmed-absent" : "deletion.provider-cleanup-pending", result.DiagnosticCode);
        Assert.Equal(verified && !retainedResource ? ElsaInstanceCleanupObservationKind.ConfirmedAbsent : ElsaInstanceCleanupObservationKind.Unknown, result.Kind);
        Assert.Equal(lifecycleOperationId, result.OperationId);
        Assert.Equal(3, result.AttemptNumber);
        if (alreadyDeleted)
            Assert.Empty(service.DeleteSubmissions);
        else
        {
            var submission = Assert.Single(service.DeleteSubmissions);
            Assert.Equal(ElsaInstanceOperationAction.Delete, submission.LifecycleAction);
            Assert.Equal(plan.Fingerprint, submission.Plan.Fingerprint);
            Assert.Equal(AzureElsaInstanceProvider.IdempotencyKey(lifecycleOperationId), submission.IdempotencyKey);
        }
    }

    [Fact]
    public async Task Cleanup_without_a_restorable_correlated_reconcile_plan_fails_closed_without_submission()
    {
        var service = new CapturingOperationService(null);
        var provider = new AzureElsaInstanceProvider(service, new CapturingOperationStore(), new InMemoryAssignmentStore(), options: EnabledOptions());
        var operationId = Guid.NewGuid();

        var result = await provider.CleanupAsync(new(
            Guid.NewGuid(), TestInstanceId, operationId, 1, null,
            new ElsaPlacementAssignmentReference(Guid.NewGuid().ToString("D")), null));

        Assert.Equal(ElsaInstanceCleanupObservationKind.Ambiguous, result.Kind);
        Assert.Equal("deletion.provider-assignment-invalid", result.DiagnosticCode);
        Assert.Empty(service.DeleteSubmissions);
    }

    private static AzureElsaInstanceProviderOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            TemplateFingerprint = new string('b', 64),
            ProviderScopeFingerprint = new string('a', 64),
            SubscriptionId = "11111111-1111-1111-1111-111111111111",
            ResourceGroupNamePrefix = "rg-elsa"
        };

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
            "westeurope",
            TestOrganizationId,
            ElsaInstanceOperationAction.Reconcile,
            operationId.ToString("D"));

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
            new string('b', 64),
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
            plan.SecretReferences,
            ProviderScopeFingerprint: new string('a', 64),
            ProviderAssignmentId: operationId);

    private sealed class CapturingOperationService(AzureProviderOperation? operation) : IAzureProviderOperationService, IAzureProviderOperationReplayService
    {
        public List<AzureProviderOperationSubmission> Submissions { get; } = [];
        public List<AzureProviderOperationSubmission> DeleteSubmissions { get; } = [];
        public AzureProviderOperation? DeleteOperation { get; init; }

        public Task<AzureProviderOperation> SubmitAsync(
            Guid workspaceId,
            AzureProviderOperationSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            return Task.FromResult(operation!);
        }

        public Task<AzureProviderOperationSubmissionResult> SubmitWithReplayAsync(
            Guid workspaceId,
            AzureProviderOperationSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            return Task.FromResult(new AzureProviderOperationSubmissionResult(operation!, Replayed: Submissions.Count > 1));
        }

        public Task<AzureProviderOperation> SubmitDeleteAsync(
            Guid workspaceId,
            AzureProviderOperationSubmission submission,
            CancellationToken cancellationToken = default)
        {
            DeleteSubmissions.Add(submission);
            return Task.FromResult(DeleteOperation ?? operation!);
        }

        public Task<AzureProviderOperationStatusResponse?> GetStatusAsync(
            Guid workspaceId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperationStatusResponse?>(null);
    }

    private sealed class ThrowingOperationService : IAzureProviderOperationService
    {
        public Task<AzureProviderOperation> SubmitAsync(Guid workspaceId, AzureProviderOperationSubmission submission, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("durable provider outcome unavailable");

        public Task<AzureProviderOperation> SubmitDeleteAsync(Guid workspaceId, AzureProviderOperationSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AzureProviderOperationStatusResponse?> GetStatusAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureProviderOperationStatusResponse?>(null);
    }

    private sealed class CapturingOperationStore(AzureProviderOperation? operation = null, AzureProviderOperation? completedDelete = null) : IAzureProviderOperationStore
    {
        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<AzureProviderOperationCreateResult> CreateOrGetWithResultAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            new(await CreateOrGetAsync(request, now, cancellationToken), Replayed: false);
        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) => Task.FromResult(completedDelete ?? operation);
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

    private sealed class InMemoryAssignmentStore : IAzureProviderResourceAssignmentStore
    {
        private AzureProviderResourceAssignment? _assignment;
        public AzureProviderAssignmentState State { get; init; } = AzureProviderAssignmentState.Reserved;
        public Guid? LastOperationId { get; init; }
        public AzureProviderResourceReferences Resources { get; init; } = new();

        public Task<AzureProviderResourceAssignment> CreateOrGetAsync(
            AzureProviderResourceAssignmentRequest request,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            _assignment ??= new(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                request.WorkspaceId,
                request.OrganizationId,
                request.InstanceId,
                request.ProviderScopeFingerprint,
                request.NamingVersion,
                request.SubscriptionId,
                AzureProviderResourceAssignmentNaming.ResourceGroupName(request.ResourceGroupNamePrefix, request.InstanceId, request.NamingVersion),
                request.WorkloadName,
                new string('f', 64),
                request.Location,
                State,
                Resources with { ResourceGroupName = AzureProviderResourceAssignmentNaming.ResourceGroupName(request.ResourceGroupNamePrefix, request.InstanceId, request.NamingVersion) },
                LastOperationId,
                1,
                now,
                now);
            return Task.FromResult(_assignment);
        }

        public Task<AzureProviderResourceAssignment?> GetAsync(
            Guid workspaceId,
            Guid assignmentId,
            CancellationToken cancellationToken = default) => Task.FromResult(_assignment);
    }
}
