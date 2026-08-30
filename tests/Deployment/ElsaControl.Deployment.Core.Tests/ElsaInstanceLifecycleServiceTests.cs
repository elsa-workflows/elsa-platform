using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class ElsaInstanceLifecycleServiceTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherWorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T10:00:00Z");

    [Fact]
    public async Task Create_replay_reuses_the_operation_instance_and_outbox()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store, new StaticTimeProvider(Now));
        var request = new ElsaInstanceCreateRequest(OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1");

        var first = await service.CreateAsync(request);
        var replay = await service.CreateAsync(request);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Instance.Id, replay.Instance.Id);
        Assert.Equal(first.Operation.Id, replay.Operation.Id);
        Assert.Equal(new ElsaLastOperationId(first.Operation.Id), first.Instance.LastOperationId);
        Assert.Equal(first.Instance.LastOperationId, replay.Instance.LastOperationId);
        Assert.Equal(first.Outbox.Id, replay.Outbox.Id);
        Assert.Single(store.Instances);
        Assert.Single(store.Operations);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_a_different_intent_is_rejected()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        await service.CreateAsync(new ElsaInstanceCreateRequest(OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));

        var act = () => service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId,
            WorkspaceId,
            "Claims copy",
            "claims-copy",
            Intent(ElsaDesiredLifecycle.Stopped),
            "create-1"));

        var exception = await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(act);
        Assert.Equal("Idempotency key was already used for a different request.", exception.Message);
        Assert.Single(store.Operations);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task Create_idempotency_binds_name_slug_and_explicit_identity()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        var instanceId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1", instanceId));

        var changedSlug = () => service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-copy", Intent(), "create-1", instanceId));

        await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(changedSlug);
        Assert.Single(store.Instances);
        Assert.Single(store.Operations);
    }

    [Theory]
    [InlineData(null, "claims-prod", "Name")]
    [InlineData(" ", "claims-prod", "Name")]
    [InlineData("Claims", null, "Slug")]
    [InlineData("Claims", "\t", "Slug")]
    public async Task Create_rejects_missing_identity_values_before_hashing(
        string? name,
        string? slug,
        string parameterName)
    {
        var service = new ElsaInstanceLifecycleService(new InMemoryElsaInstanceLifecycleStore());
        var request = new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, name!, slug!, Intent(), "create-invalid");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public async Task Stale_if_match_version_cannot_accept_a_lifecycle_mutation()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));

        var act = () => service.StopAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId,
            created.Instance.Id,
            created.Instance.Version + 1,
            "stop-1"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Instance version conflict.", exception.Message);
        Assert.Single(store.Operations);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task Instance_lookup_and_idempotency_are_workspace_scoped()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        var first = await service.CreateAsync(new ElsaInstanceCreateRequest(OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));

        var wrongWorkspace = () => service.StopAsync(new ElsaInstanceLifecycleRequest(
            OtherWorkspaceId,
            first.Instance.Id,
            first.Instance.Version,
            "stop-1"));
        await Assert.ThrowsAsync<KeyNotFoundException>(wrongWorkspace);

        var second = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId,
            OtherWorkspaceId,
            "Claims",
            "claims-prod",
            Intent(),
            "create-1"));

        Assert.NotEqual(first.Instance.Id, second.Instance.Id);
        Assert.Equal(2, store.Instances.Count);
        Assert.Equal(2, store.Operations.Count);
        Assert.Equal(2, store.Outbox.Count);
    }

    [Fact]
    public async Task Recover_reuses_the_recovery_required_operation_and_records_new_work()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store, new StaticTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        var accepted = store.Operations.Single();
        var progressing = accepted;
        foreach (var state in new[]
                 {
                     ElsaInstanceOperationState.Queued,
                     ElsaInstanceOperationState.Running,
                     ElsaInstanceOperationState.RecoveryRequired
                 })
        {
            progressing = progressing.TransitionTo(state);
            await store.CommitAcceptedAsync(
                created.Instance,
                created.Instance,
                progressing,
                new ElsaInstanceLifecycleOutboxMessage(
                    Guid.NewGuid(),
                    WorkspaceId,
                    created.Instance.Id,
                    accepted.Id,
                    accepted.Action,
                    accepted.RequestHash,
                    Now.AddMinutes(store.Outbox.Count)));
        }
        await new ElsaInstanceProviderReconciliationService(
                store,
                new StaticProviderPort(new(
                    ElsaInstanceProviderObservationKind.Unknown,
                    ElsaObservedLifecycle.Unknown,
                    ElsaInstanceProviderHealthGate.Unknown,
                    "retry-proof-observation",
                    new ElsaInstanceProviderRetryEvidence(
                        "https://evidence.example.test/recovery/retry-proof",
                        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"))),
                new StaticTimeProvider(Now))
            .ReconcileAsync(WorkspaceId, accepted.Id);

        var recovered = await service.RecoverAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId,
            created.Instance.Id,
            store.Instances.Single().Version,
            "recover-1"));

        Assert.False(recovered.Replayed);
        Assert.Equal(accepted.Id, recovered.Operation.Id);
        Assert.Equal(ElsaInstanceOperationState.Queued, recovered.Operation.State);
        Assert.Equal(2, recovered.Operation.AttemptNumber);
        Assert.Equal(5, store.Outbox.Count);
        Assert.Single(store.Operations);
    }

    [Fact]
    public async Task Recover_without_provider_retry_proof_is_rejected()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store, new StaticTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        store.MarkRecoveryRequired(created.Operation.Id);

        var error = await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => service.RecoverAsync(new(
            WorkspaceId, created.Instance.Id, created.Instance.Version, "recover-without-proof")));

        Assert.Equal("Provider reconciliation has not established that retry is safe.", error.Message);
    }

    [Fact]
    public async Task Recover_delete_requeues_cleanup_without_provider_reconciliation_evidence()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store, new StaticTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        var progressing = created.Operation;
        foreach (var state in new[]
                 {
                     ElsaInstanceOperationState.Queued,
                     ElsaInstanceOperationState.Running,
                     ElsaInstanceOperationState.Succeeded
                 })
        {
            progressing = progressing.TransitionTo(state);
            await store.CommitAcceptedAsync(
                created.Instance,
                created.Instance,
                progressing,
                new ElsaInstanceLifecycleOutboxMessage(
                    Guid.NewGuid(),
                    WorkspaceId,
                    created.Instance.Id,
                    created.Operation.Id,
                    created.Operation.Action,
                    created.Operation.RequestHash,
                    Now.AddMinutes(store.Outbox.Count)));
        }

        var deletion = await service.DeleteAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId, created.Instance.Id, store.Instances.Single().Version, "delete-1"));
        store.MarkRecoveryRequired(deletion.Operation.Id);

        var recovered = await service.RecoverAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId, created.Instance.Id, store.Instances.Single().Version, "recover-delete-1"));

        Assert.Equal(deletion.Operation.Id, recovered.Operation.Id);
        Assert.Equal(ElsaInstanceOperationState.Queued, recovered.Operation.State);
        Assert.Equal(2, recovered.Operation.AttemptNumber);
    }

    [Fact]
    public async Task Active_reservation_is_selected_before_a_waiting_delete_successor()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        var deletion = await service.DeleteAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId, created.Instance.Id, created.Instance.Version, "delete-1"));

        Assert.Equal(ElsaInstanceOperationState.WaitingForPriorOperation, deletion.Operation.State);
        var active = await store.GetActiveOperationAsync(WorkspaceId, created.Instance.Id);
        Assert.NotNull(active);
        Assert.Equal(created.Operation.Id, active!.Id);
        Assert.True(active.HoldsReservation);
    }

    [Fact]
    public async Task Delete_replay_uses_the_original_operation_after_it_mutates_the_instance_intent()
    {
        var store = new InMemoryElsaInstanceLifecycleStore();
        var service = new ElsaInstanceLifecycleService(store);
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        var request = new ElsaInstanceLifecycleRequest(
            WorkspaceId, created.Instance.Id, created.Instance.Version, "delete-1");

        var accepted = await service.DeleteAsync(request);
        var replay = await service.DeleteAsync(request);

        Assert.False(accepted.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(accepted.Operation.Id, replay.Operation.Id);
    }

    [Fact]
    public async Task Waiting_delete_is_claimed_after_its_prior_operation_becomes_terminal()
    {
        var store = new InMemoryElsaInstanceLifecycleStore(new StaticTimeProvider(Now));
        var service = new ElsaInstanceLifecycleService(store);
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            OrganizationId, WorkspaceId, "Claims", "claims-prod", Intent(), "create-1"));
        var deletion = await service.DeleteAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId, created.Instance.Id, created.Instance.Version, "delete-1"));

        var priorWork = await store.TryClaimNextAsync("lifecycle-worker-1", Now);
        Assert.NotNull(priorWork);
        await store.FailResolutionAsync(new ElsaInstanceLifecycleResolutionFailure(
            priorWork!.Outbox.WorkspaceId,
            priorWork.Outbox.InstanceId,
            priorWork.Operation.Id,
            priorWork.Outbox.Id,
            priorWork.Outbox.RequestHash,
            "lifecycle-worker-1",
            "resolution.failed",
            "Lifecycle plan resolution was rejected.",
            Now,
            priorWork.LeaseToken,
            priorWork.LeaseVersion));

        var claimed = await store.TryClaimNextDeletionAsync("deletion-worker-1", Now);

        Assert.NotNull(claimed);
        Assert.Equal(deletion.Operation.Id, claimed!.Operation.Id);
        Assert.Equal(ElsaInstanceOperationState.Accepted, claimed.Operation.State);
        Assert.True(claimed.CanFinalizeLocally);
    }

    private static ElsaInstanceIntent Intent(ElsaDesiredLifecycle lifecycle = ElsaDesiredLifecycle.Running) => new(
        new ElsaReleaseIntent("valence-runtime", "3.8", channel: "stable"),
        new ElsaApplicationIntent("combined", "starter", packagePolicy: "approved"),
        new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed"),
        lifecycle);

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticProviderPort(ElsaInstanceProviderObservation observation)
        : IElsaInstanceProviderReconciliationPort
    {
        public Task<ElsaInstanceProviderObservation> ObserveAsync(
            ElsaInstanceProviderReconciliationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(observation.Correlate(request));
    }
}
