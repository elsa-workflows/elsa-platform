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

        var recovered = await service.RecoverAsync(new ElsaInstanceLifecycleRequest(
            WorkspaceId,
            created.Instance.Id,
            created.Instance.Version,
            "recover-1"));

        Assert.False(recovered.Replayed);
        Assert.Equal(accepted.Id, recovered.Operation.Id);
        Assert.Equal(ElsaInstanceOperationState.Queued, recovered.Operation.State);
        Assert.Equal(2, recovered.Operation.AttemptNumber);
        Assert.Equal(5, store.Outbox.Count);
        Assert.Single(store.Operations);
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

    private static ElsaInstanceIntent Intent(ElsaDesiredLifecycle lifecycle = ElsaDesiredLifecycle.Running) => new(
        new ElsaReleaseIntent("valence-runtime", "3.8", channel: "stable"),
        new ElsaApplicationIntent("combined", "starter", packagePolicy: "approved"),
        new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed"),
        lifecycle);

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
