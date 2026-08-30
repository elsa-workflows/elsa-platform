using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class ElsaInstanceLifecycleStoreTests
{
    [Fact]
    public async Task Create_commits_instance_revision_operation_and_outbox_and_replays_exactly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Lifecycle workspace");
        var instanceId = Guid.NewGuid();
        var request = new ElsaInstanceCreateRequest(
            workspace.OrganizationId,
            workspace.Id,
            "Managed Elsa",
            "managed-elsa",
            CreateIntent(),
            "create-lifecycle",
            instanceId);
        var service = new ElsaInstanceLifecycleService(new EfCoreElsaInstanceLifecycleStore(db));

        var first = await service.CreateAsync(request);
        db.ChangeTracker.Clear();

        Assert.False(first.Replayed);
        Assert.Equal(instanceId, first.Instance.Id);
        Assert.NotNull(first.Instance.DesiredStateRevisionId);
        Assert.Equal(first.Operation.Id, first.Outbox.OperationId);
        Assert.Equal(first.Operation.RequestHash, first.Outbox.RequestHash);
        Assert.Equal(first.Instance.DesiredStateRevisionId!.Value.Value,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == first.Operation.Id)).DesiredStateRevisionId);
        Assert.Equal(1, await db.ElsaInstances.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceIntentRevisions.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceOperations.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceLifecycleOutbox.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceAuditEvents.CountAsync());

        var reloaded = await new EfCoreElsaInstanceLifecycleStore(db)
            .GetInstanceAsync(workspace.Id, instanceId);
        Assert.NotNull(reloaded);
        Assert.Equal("3.10", reloaded!.ReleaseIntent.ReleaseLine);
        Assert.Equal(ElsaFeatureOverrideKind.Number, reloaded.ApplicationIntent.FeatureOverrides["replicas"].Kind);
        Assert.Equal("3", reloaded.ApplicationIntent.FeatureOverrides["replicas"].Value);
        Assert.Equal(first.Instance.ComputeCanonicalIntentHash(), reloaded.ComputeCanonicalIntentHash());

        var replay = await service.CreateAsync(request);

        Assert.True(replay.Replayed);
        Assert.Equal(first.Instance.Id, replay.Instance.Id);
        Assert.Equal(first.Operation.Id, replay.Operation.Id);
        Assert.Equal(first.Outbox.Id, replay.Outbox.Id);
        Assert.Equal(first.Instance.Version, replay.Instance.Version);
        Assert.Equal(1, await db.ElsaInstanceIntentRevisions.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceOperations.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceLifecycleOutbox.CountAsync());
    }

    [Fact]
    public async Task Accepted_operation_links_the_existing_revision_when_intent_hash_is_unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Revision link workspace");
        var service = new ElsaInstanceLifecycleService(new EfCoreElsaInstanceLifecycleStore(db));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Managed Elsa", "revision-link-elsa", CreateIntent(), "create-revision-link"));
        await CompleteOperationAsync(db, created.Operation.Id);

        db.ChangeTracker.Clear();
        var updated = await service.UpdateIntentAsync(new ElsaInstanceIntentUpdateRequest(
            workspace.Id, created.Instance.Id, CreateIntent(), created.Instance.Version, "update-revision-link"));

        Assert.False(updated.Replayed);
        Assert.Equal(created.Instance.Version + 1, updated.Instance.Version);
        Assert.Equal(1, await db.ElsaInstanceIntentRevisions.CountAsync());
        Assert.Equal(created.Instance.DesiredStateRevisionId!.Value.Value,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == updated.Operation.Id)).DesiredStateRevisionId);
        Assert.Equal(2, await db.ElsaInstanceAuditEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_operations_for_one_instance_have_one_winner_and_no_partial_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateMigratedContext(connection);
        await setup.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(setup, "Concurrent lifecycle workspace");
        var createService = new ElsaInstanceLifecycleService(new EfCoreElsaInstanceLifecycleStore(setup));
        var created = await createService.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Managed Elsa", "concurrent-elsa", CreateIntent(), "create-concurrent"));
        await CompleteOperationAsync(setup, created.Operation.Id);
        setup.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var firstDb = new CatalogDbContext(options);
        await using var secondDb = new CatalogDbContext(options);
        var firstStore = new EfCoreElsaInstanceLifecycleStore(firstDb);
        var secondStore = new EfCoreElsaInstanceLifecycleStore(secondDb);
        var firstExpected = await firstStore.GetInstanceAsync(workspace.Id, created.Instance.Id);
        var secondExpected = await secondStore.GetInstanceAsync(workspace.Id, created.Instance.Id);
        Assert.NotNull(firstExpected);
        Assert.NotNull(secondExpected);
        var firstTransition = ElsaInstanceStateMachine.Request(
            firstExpected!, ElsaInstanceOperationAction.Reconcile, expectedVersion: firstExpected.Version,
            idempotencyKey: "reconcile-race-1", requestHash: RequestHash("reconcile-race-1"));
        var secondTransition = ElsaInstanceStateMachine.Request(
            secondExpected!, ElsaInstanceOperationAction.Reconcile, expectedVersion: secondExpected.Version,
            idempotencyKey: "reconcile-race-2", requestHash: RequestHash("reconcile-race-2"));

        var results = await Task.WhenAll(
            CaptureAsync(() => firstStore.CommitAcceptedAsync(
                firstExpected, firstTransition.Instance, firstTransition.Operation,
                NewOutbox(firstTransition))),
            CaptureAsync(() => secondStore.CommitAcceptedAsync(
                secondExpected, secondTransition.Instance, secondTransition.Operation,
                NewOutbox(secondTransition))));

        Assert.Equal(1, results.Count(x => x.Error is null));
        var conflict = Assert.Single(results, x => x.Error is not null).Error;
        Assert.IsType<ElsaInstanceLifecycleConflictException>(conflict);
        await using var verify = new CatalogDbContext(options);
        Assert.Equal(2, await verify.ElsaInstanceOperations.CountAsync());
        Assert.Equal(2, await verify.ElsaInstanceLifecycleOutbox.CountAsync());
        Assert.Equal(2, await verify.ElsaInstanceAuditEvents.CountAsync());
        Assert.Equal(1, await verify.ElsaInstanceIntentRevisions.CountAsync());
        Assert.Equal(1, await verify.ElsaInstanceOperations.CountAsync(x => x.State == ElsaInstanceOperationState.Accepted));
    }

    [Fact]
    public async Task Commit_rechecks_expected_version_after_the_preflight_read()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateMigratedContext(connection);
        await setup.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(setup, "Version lifecycle workspace");
        var created = await new ElsaInstanceLifecycleService(new EfCoreElsaInstanceLifecycleStore(setup)).CreateAsync(
            new ElsaInstanceCreateRequest(workspace.OrganizationId, workspace.Id, "Managed Elsa", "version-elsa", CreateIntent(), "create-version"));
        await CompleteOperationAsync(setup, created.Operation.Id);
        setup.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var firstDb = new CatalogDbContext(options);
        await using var secondDb = new CatalogDbContext(options);
        var firstStore = new EfCoreElsaInstanceLifecycleStore(firstDb);
        var secondStore = new EfCoreElsaInstanceLifecycleStore(secondDb);
        var expected = await firstStore.GetInstanceAsync(workspace.Id, created.Instance.Id);
        var secondExpected = await secondStore.GetInstanceAsync(workspace.Id, created.Instance.Id);
        Assert.NotNull(expected);
        Assert.NotNull(secondExpected);

        var firstTransition = ElsaInstanceStateMachine.Request(
            expected!, ElsaInstanceOperationAction.Reconcile, expectedVersion: expected.Version,
            idempotencyKey: "version-reconcile-1", requestHash: RequestHash("version-reconcile-1"));
        var firstOutbox = NewOutbox(firstTransition);
        await firstStore.CommitAcceptedAsync(expected, firstTransition.Instance, firstTransition.Operation, firstOutbox);
        await CompleteOperationAsync(setup, firstTransition.Operation.Id);

        var secondTransition = ElsaInstanceStateMachine.Request(
            secondExpected!, ElsaInstanceOperationAction.Reconcile, expectedVersion: secondExpected.Version,
            idempotencyKey: "version-reconcile-2", requestHash: RequestHash("version-reconcile-2"));
        var secondOutbox = NewOutbox(secondTransition);
        var error = await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() =>
            secondStore.CommitAcceptedAsync(secondExpected, secondTransition.Instance, secondTransition.Operation, secondOutbox));

        Assert.Equal("Instance version conflict.", error.Message);
    }

    [Fact]
    public async Task Recover_persists_reconciled_instance_observation_with_the_operation_resume()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Recovery lifecycle workspace");
        var service = new ElsaInstanceLifecycleService(new EfCoreElsaInstanceLifecycleStore(db));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Managed Elsa", "recovery-elsa", CreateIntent(), "create-recovery"));

        db.ChangeTracker.Clear();
        var instance = await db.ElsaInstances.SingleAsync(x => x.Id == created.Instance.Id);
        instance.ObservedLifecycle = ElsaObservedLifecycle.Unknown;
        instance.Health = ElsaInstanceHealth.Unknown;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == created.Operation.Id);
        operation.State = ElsaInstanceOperationState.Queued;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == created.Operation.Id);
        operation.State = ElsaInstanceOperationState.Running;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == created.Operation.Id);
        operation.State = ElsaInstanceOperationState.RecoveryRequired;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var current = await new EfCoreElsaInstanceLifecycleStore(db)
            .GetInstanceAsync(workspace.Id, created.Instance.Id);
        Assert.NotNull(current);
        var recovered = await service.RecoverAsync(new ElsaInstanceLifecycleRequest(
            workspace.Id, created.Instance.Id, current!.Version, "recover-recovery"));

        Assert.False(recovered.Replayed);
        Assert.Equal(ElsaObservedLifecycle.Provisioning, recovered.Instance.ObservedLifecycle);
        Assert.Equal(ElsaInstanceOperationState.Queued, recovered.Operation.State);
        Assert.Equal(2, recovered.Operation.AttemptNumber);
        Assert.Equal(created.Outbox.Id, recovered.Outbox.Id);
        db.ChangeTracker.Clear();
        var persisted = await new EfCoreElsaInstanceLifecycleStore(db)
            .GetInstanceAsync(workspace.Id, created.Instance.Id);
        Assert.Equal(ElsaObservedLifecycle.Provisioning, persisted!.ObservedLifecycle);
        Assert.Equal(3, persisted.Version);
        Assert.Equal(1, await db.ElsaInstanceLifecycleOutbox.CountAsync());
        Assert.Equal(2, await db.ElsaInstanceAuditEvents.CountAsync());
    }

    private static async Task<Workspace> CreateWorkspaceAsync(CatalogDbContext db, string name)
    {
        var workspace = new Workspace { Name = name };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        return workspace;
    }

    private static async Task CompleteOperationAsync(CatalogDbContext db, Guid operationId)
    {
        db.ChangeTracker.Clear();
        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == operationId);
        operation.State = ElsaInstanceOperationState.Succeeded;
        await db.SaveChangesAsync();
    }

    private static ElsaInstanceLifecycleOutboxMessage NewOutbox(
        ElsaInstanceTransitionResult transition) => new(
        Guid.NewGuid(),
        transition.Instance.WorkspaceId,
        transition.Instance.Id,
        transition.Operation.Id,
        transition.Operation.Action,
        transition.Operation.RequestHash,
        DateTimeOffset.UtcNow);

    private static string RequestHash(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static async Task<Captured<T>> CaptureAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return new Captured<T>(await action(), null);
        }
        catch (Exception error)
        {
            return new Captured<T>(default, error);
        }
    }

    private static ElsaInstanceIntent CreateIntent() => new(
        new ElsaReleaseIntent("server-studio", "3.10", "3.10.4"),
        new ElsaApplicationIntent(
            "combined",
            "starter",
            new Dictionary<string, ElsaFeatureOverride> { ["replicas"] = ElsaFeatureOverride.FromNumber(3) },
            "approved"),
        new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed"));

    private static CatalogDbContext CreateMigratedContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        return new CatalogDbContext(options);
    }

    private sealed record Captured<T>(T? Value, Exception? Error);
}
