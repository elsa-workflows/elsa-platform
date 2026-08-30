using ElsaControl.Deployment.Azure;
using System.Text.Json;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationServiceTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submit_persists_only_the_safe_provider_plan_projection()
    {
        var store = new CapturingStore();
        var service = new AzureProviderOperationService(store, new FixedTimeProvider(Now));

        var result = await service.SubmitAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission(
                "request-1",
                new('b', 64),
                CreatePlan()));

        Assert.Equal(AzureProviderOperationAction.Reconcile, result.Action);
        Assert.Equal("oci://evidence.example/manifest", store.Request!.ReleaseManifestReference);
        Assert.Equal("oci://evidence.example/signature", store.Request.ReleaseManifestSignatureReference);
        Assert.Equal("secret://vault/database", store.Request.SecretReferences!["database:connectionstring"]);
        Assert.Null(store.Request.GetType().GetProperty("RawPayload"));
    }

    [Theory]
    [InlineData("secret://vault/../database")]
    [InlineData("secret://vault/database%2Fconnection")]
    [InlineData("secret://user:password@vault/database")]
    [InlineData("secret://vault/database?version=1")]
    public async Task Submit_rejects_unsafe_secret_locators(string locator)
    {
        var service = new AzureProviderOperationService(new CapturingStore(), new FixedTimeProvider(Now));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission("request-1", new('b', 64), CreatePlan() with
            {
                SecretReferences = new Dictionary<string, string> { ["database:connectionstring"] = locator }
            })));

        Assert.Contains("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_rejects_noncanonical_secret_reference_keys()
    {
        var service = new AzureProviderOperationService(new CapturingStore(), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission("request-1", new('b', 64), CreatePlan() with
            {
                SecretReferences = new Dictionary<string, string>
                {
                    ["Database:ConnectionString"] = "secret://vault/database"
                }
            })));
    }

    [Fact]
    public async Task Delete_submission_uses_the_same_idempotent_operation_contract()
    {
        var store = new CapturingStore();
        var service = new AzureProviderOperationService(store, new FixedTimeProvider(Now));

        var result = await service.SubmitDeleteAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission("delete-1", new('b', 64), CreatePlan()));

        Assert.Equal(AzureProviderOperationAction.Delete, result.Action);
        Assert.Equal(AzureProviderOperationAction.Delete, store.Request!.Action);
        Assert.Equal("delete-1:delete", store.Request.IdempotencyKey);
    }

    [Fact]
    public async Task Delete_submission_derives_a_bounded_deterministic_key_from_a_maximum_length_key()
    {
        var originalKey = new string('x', 512);
        var firstStore = new CapturingStore();
        var secondStore = new CapturingStore();

        await new AzureProviderOperationService(firstStore, new FixedTimeProvider(Now)).SubmitDeleteAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission(originalKey, new('b', 64), CreatePlan()));
        await new AzureProviderOperationService(secondStore, new FixedTimeProvider(Now)).SubmitDeleteAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission(originalKey, new('b', 64), CreatePlan()));

        Assert.StartsWith("delete:sha256:", firstStore.Request!.IdempotencyKey, StringComparison.Ordinal);
        Assert.True(firstStore.Request.IdempotencyKey.Length <= 512);
        Assert.Equal(firstStore.Request.IdempotencyKey, secondStore.Request!.IdempotencyKey);
    }

    [Theory]
    [InlineData(513)]
    [InlineData(0)]
    public async Task Delete_submission_rejects_invalid_original_idempotency_keys(int length)
    {
        var key = length == 0 ? "\t" : new string('x', length);
        var service = new AzureProviderOperationService(new CapturingStore(), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitDeleteAsync(
            WorkspaceId,
            new AzureProviderOperationSubmission(key, new('b', 64), CreatePlan())));
    }

    [Fact]
    public void Operation_json_does_not_include_secret_locators_or_recovery_only_projection()
    {
        var operation = new AzureProviderOperation(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            WorkspaceId,
            "workload-a",
            AzureProviderOperationAction.Reconcile,
            "request-1",
            new('a', 64),
            new('b', 64),
            new('c', 64),
            new('d', 64),
            "3.8.0",
            "3.8",
            "combined",
            "Dedicated",
            "westeurope",
            "valenceruntimeimages.azurecr.io/runtime-combined",
            "sha256:" + new string('e', 64),
            "sha256:" + new string('f', 64),
            "sha256:" + new string('a', 64),
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
            Now,
            Now,
            null,
            "oci://evidence.example/manifest",
            "oci://evidence.example/signature",
            new Dictionary<string, string> { ["database:connectionstring"] = "secret://vault/database" });

        var json = JsonSerializer.Serialize(operation);

        Assert.DoesNotContain("SecretReferences", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret://vault/database", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeSecretReferences", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_safe_secret_references_cannot_be_mutated_across_operations()
    {
        var operation = await new CapturingStore().CreateOrGetAsync(
            AzureProviderOperationValidation.Normalize(new AzureProviderOperationRequest(
                WorkspaceId,
                "workload-a",
                AzureProviderOperationAction.Reconcile,
                "request-empty-secrets",
                new('a', 64),
                new('b', 64),
                "3.8.0",
                "3.8",
                "combined",
                "Dedicated",
                "westeurope",
                "valenceruntimeimages.azurecr.io/runtime-combined",
                "sha256:" + new string('e', 64))),
            Now);
        var mutableView = Assert.IsAssignableFrom<IDictionary<string, string>>(operation.SafeSecretReferences);

        Assert.Throws<NotSupportedException>(() => mutableView.Add("database", "secret://vault/database"));
        Assert.Empty(operation.SafeSecretReferences);
    }

    private static AzureWorkloadPlan CreatePlan() => new(
        "workload-a",
        "westeurope",
        "3.8.0",
        "3.8",
        "combined",
        "Dedicated",
        "valenceruntimeimages.azurecr.io/runtime-combined",
        new('c', 64),
        "oci://evidence.example/manifest",
        "sha256:" + new string('d', 64),
        "oci://evidence.example/signature",
        "sha256:" + new string('e', 64),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["database:connectionstring"] = "secret://vault/database"
        },
        new('a', 64));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingStore : IAzureProviderOperationStore
    {
        public AzureProviderOperationRequest? Request { get; private set; }
        private AzureProviderOperation? _operation;

        public Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Request = AzureProviderOperationValidation.Normalize(request);
            _operation ??= new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Request.WorkspaceId,
                Request.TargetKey,
                Request.Action,
                Request.IdempotencyKey,
                AzureProviderOperationValidation.ComputeRequestHash(Request),
                AzureProviderOperationValidation.ComputeOperationIdentity(Request),
                Request.PlanFingerprint,
                Request.TemplateFingerprint,
                Request.ElsaVersion,
                Request.ReleaseLine,
                Request.Topology,
                Request.Isolation,
                Request.Location,
                Request.ImageRepository,
                Request.ImageDigest,
                Request.ReleaseManifestDigest,
                Request.ReleaseManifestSignatureDigest,
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
                null,
                Request.ReleaseManifestReference,
                Request.ReleaseManifestSignatureReference,
                Request.SecretReferences);
            return Task.FromResult(_operation);
        }

        public Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_operation is { WorkspaceId: var currentWorkspace, Id: var currentId } && currentWorkspace == workspaceId && currentId == operationId ? _operation : null);

        public Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default) => Task.FromResult<AzureProviderOperation?>(null);
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
