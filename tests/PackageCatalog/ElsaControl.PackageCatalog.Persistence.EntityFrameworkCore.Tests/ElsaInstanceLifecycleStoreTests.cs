using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class ElsaInstanceLifecycleStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T10:00:00Z");

    [Fact]
    public void Active_run_reservation_index_includes_unmanaged_runs()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateMigratedContext(connection);
        var index = db.Model.FindEntityType(typeof(DeploymentRunEntity))!
            .GetIndexes()
            .Single(x => x.Properties.Select(property => property.Name)
                .SequenceEqual(["WorkspaceId", "EnvironmentId"]));

        Assert.Equal("Status IN ('Queued', 'Running', 'RecoveryRequired')", index.GetFilter());
    }

    [Fact]
    public void Sql_server_resolved_plan_trigger_migration_is_idempotent_and_append_only()
    {
        // SQL Server is not a local integration dependency; assert the exact
        // generated SQL operation so the production migration remains reviewable.
        var migration = new AddElsaInstanceWorkerPersistence();
        var up = Assert.Single(migration.UpOperations.OfType<SqlOperation>(),
            x => x.Sql.Contains("TR_ElsaInstanceResolvedPlans_AppendOnly", StringComparison.Ordinal));
        var down = Assert.Single(migration.DownOperations.OfType<SqlOperation>(),
            x => x.Sql.Contains("TR_ElsaInstanceResolvedPlans_AppendOnly", StringComparison.Ordinal));

        Assert.Contains("IF OBJECT_ID(N'dbo.TR_ElsaInstanceResolvedPlans_AppendOnly', N'TR') IS NULL", up.Sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER dbo.TR_ElsaInstanceResolvedPlans_AppendOnly", up.Sql, StringComparison.Ordinal);
        Assert.Contains("ON dbo.ElsaInstanceResolvedPlans", up.Sql, StringComparison.Ordinal);
        Assert.Contains("INSTEAD OF UPDATE, DELETE", up.Sql, StringComparison.Ordinal);
        Assert.Contains("DROP TRIGGER IF EXISTS dbo.TR_ElsaInstanceResolvedPlans_AppendOnly", down.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_store_requires_a_governed_resolution_source_at_composition()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EfCoreElsaInstanceLifecycleStore(null!, null!));
    }

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
        var service = new ElsaInstanceLifecycleService(CreateStore(db));

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

        var reloaded = await CreateStore(db)
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
        var service = new ElsaInstanceLifecycleService(CreateStore(db));
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
        var createService = new ElsaInstanceLifecycleService(CreateStore(setup));
        var created = await createService.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Managed Elsa", "concurrent-elsa", CreateIntent(), "create-concurrent"));
        await CompleteOperationAsync(setup, created.Operation.Id);
        setup.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var firstDb = new CatalogDbContext(options);
        await using var secondDb = new CatalogDbContext(options);
        var firstStore = CreateStore(firstDb);
        var secondStore = CreateStore(secondDb);
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
        var created = await new ElsaInstanceLifecycleService(CreateStore(setup)).CreateAsync(
            new ElsaInstanceCreateRequest(workspace.OrganizationId, workspace.Id, "Managed Elsa", "version-elsa", CreateIntent(), "create-version"));
        await CompleteOperationAsync(setup, created.Operation.Id);
        setup.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var firstDb = new CatalogDbContext(options);
        await using var secondDb = new CatalogDbContext(options);
        var firstStore = CreateStore(firstDb);
        var secondStore = CreateStore(secondDb);
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
        var service = new ElsaInstanceLifecycleService(CreateStore(db));
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
        operation.FailureCode = ElsaInstanceProviderReconciliationService.RetrySafeCode;
        operation.ReconciliationRetryEvidenceReference = "https://evidence.example/retry/recovery-elsa";
        operation.ReconciliationRetryEvidenceDigest = "sha256:" + new string('a', 64);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var current = await CreateStore(db)
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
        var persisted = await CreateStore(db)
            .GetInstanceAsync(workspace.Id, created.Instance.Id);
        Assert.Equal(ElsaObservedLifecycle.Provisioning, persisted!.ObservedLifecycle);
        Assert.Equal(3, persisted.Version);
        Assert.Equal(1, await db.ElsaInstanceLifecycleOutbox.CountAsync());
        Assert.Equal(2, await db.ElsaInstanceAuditEvents.CountAsync());
    }

    [Fact]
    public async Task Worker_claims_and_commits_plan_run_and_environment_reservation_atomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Worker workspace");
        var instance = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "worker-elsa", WorkerIntent(), "worker-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, instance.Instance.Id);
        db.ChangeTracker.Clear();

        var source = new StaticResolutionInputSource(instance.Instance, target);
        var store = new EfCoreElsaInstanceLifecycleStore(db, source, new FixedTimeProvider(Now));
        var check = SuccessfulResolution(workspace.Id, instance.Instance.Id);
        Assert.Empty(ResolvedElsaApplicationPlanValidator.Validate(check.Plan!));
        var result = await new ElsaInstanceLifecycleWorker(
                store,
                new StaticResolver(check),
                new FixedTimeProvider(Now))
            .ProcessAvailableAsync("worker-one");

        var completed = Assert.Single(result.Results);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, completed.Outcome);
        Assert.Equal(ElsaInstanceOperationState.Queued, completed.Operation.State);
        Assert.NotNull(completed.Run);
        Assert.Equal(instance.Instance.Id, (await db.DeploymentRuns.SingleAsync()).ElsaInstanceId);
        Assert.Equal(target.EnvironmentId, completed.Run!.EnvironmentId);
        Assert.Equal(1, await db.ElsaInstanceResolvedPlans.CountAsync());
        Assert.Equal(2, (await CreateStore(db)
            .GetInstanceAsync(workspace.Id, instance.Instance.Id))!.Version);
        Assert.Equal(2, await db.ElsaInstanceAuditEvents.CountAsync());
        Assert.Equal(0, result.ProviderInvocations);
    }

    [Fact]
    public async Task Worker_commit_replay_returns_existing_run_without_mutating_immutable_plan()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Replay workspace");
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "replay-worker-elsa", WorkerIntent(), "replay-worker-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, accepted.Instance.Id);
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(accepted.Instance, target),
            new FixedTimeProvider(Now.AddSeconds(1)));
        var item = await store.TryClaimNextAsync("worker-one", Now)
            ?? throw new InvalidOperationException("Expected a claimed work item.");
        var resolved = SuccessfulResolution(workspace.Id, accepted.Instance.Id);
        var commit = CreateResolutionCommit(item, resolved, Now.AddSeconds(1));

        var first = await store.CommitResolvedAsync(commit);
        var replay = await store.CommitResolvedAsync(commit);

        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, first.Outcome);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.AlreadyCompleted, replay.Outcome);
        Assert.Equal(first.Run!.Id, replay.Run!.Id);
        Assert.Equal(1, await db.DeploymentRuns.CountAsync());
        Assert.Equal(1, await db.ElsaInstanceResolvedPlans.CountAsync());
    }

    [Fact]
    public async Task Finalization_uses_store_clock_when_caller_supplies_a_backdated_timestamp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Authoritative clock workspace");
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "authoritative-clock-elsa", WorkerIntent(), "authoritative-clock-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, accepted.Instance.Id);
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(accepted.Instance, target),
            new FixedTimeProvider(Now.AddMinutes(6)));
        var item = await store.TryClaimNextAsync("worker-one", Now)
            ?? throw new InvalidOperationException("Expected a claimed work item.");

        var error = await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => store.CommitResolvedAsync(
            CreateResolutionCommit(item, SuccessfulResolution(workspace.Id, accepted.Instance.Id), Now.AddMinutes(1))));

        Assert.Equal("Lifecycle work item is no longer owned by this worker.", error.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(ElsaInstanceOperationState.Accepted,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == accepted.Operation.Id)).State);
        Assert.Empty(await db.DeploymentRuns.ToListAsync());
    }

    [Fact]
    public async Task Worker_rejects_stale_lease_token_without_partial_finalization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Stale lease workspace");
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "stale-lease-elsa", WorkerIntent(), "stale-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, accepted.Instance.Id);
        var source = new StaticResolutionInputSource(accepted.Instance, target);
        var store = new EfCoreElsaInstanceLifecycleStore(db, source);
        var item = await store.TryClaimNextAsync("worker-one", Now)
            ?? throw new InvalidOperationException("Expected a claimed work item.");
        var resolved = SuccessfulResolution(workspace.Id, accepted.Instance.Id);
        var commit = CreateResolutionCommit(item, resolved, Now.AddSeconds(1)) with
        {
            LeaseToken = new string('a', 64)
        };

        var error = await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => store.CommitResolvedAsync(commit));

        Assert.Equal("Lifecycle work item is no longer owned by this worker.", error.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(ElsaInstanceOperationState.Accepted,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == item.Operation.Id)).State);
        Assert.Empty(await db.DeploymentRuns.ToListAsync());
        Assert.Empty(await db.ElsaInstanceResolvedPlans.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_workers_only_claim_one_accepted_outbox()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateMigratedContext(connection);
        await setup.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(setup, "Concurrent worker workspace");
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(setup), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "concurrent-worker-elsa", WorkerIntent(), "concurrent-worker-create"));
        var target = await AddManagedEnvironmentAsync(setup, workspace, accepted.Instance.Id);
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var firstDb = new CatalogDbContext(options);
        await using var secondDb = new CatalogDbContext(options);
        var first = new EfCoreElsaInstanceLifecycleStore(firstDb, new StaticResolutionInputSource(accepted.Instance, target));
        var second = new EfCoreElsaInstanceLifecycleStore(secondDb, new StaticResolutionInputSource(accepted.Instance, target));

        var claims = await Task.WhenAll(
            CaptureAsync(() => first.TryClaimNextAsync("worker-one", Now)),
            CaptureAsync(() => second.TryClaimNextAsync("worker-two", Now)));

        Assert.Equal(1, claims.Count(x => x.Error is null && x.Value is not null));
        Assert.DoesNotContain(claims, x => x.Error is not null);
        await using var verify = new CatalogDbContext(options);
        Assert.Equal(1, await verify.ElsaInstanceOperations.CountAsync(x => x.WorkerId != null));
        Assert.Equal(0, await verify.ElsaInstanceOperations.CountAsync(x => x.State != ElsaInstanceOperationState.Accepted));
    }

    [Fact]
    public async Task Expired_resolver_claim_is_reclaimed_with_rotated_lease_and_stale_worker_cannot_finalize()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Expired lease workspace");
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Worker Elsa", "expired-lease-elsa", WorkerIntent(), "expired-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, accepted.Instance.Id);
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(accepted.Instance, target),
            new FixedTimeProvider(Now.AddMinutes(7)));

        var first = await store.TryClaimNextAsync("worker-one", Now)
            ?? throw new InvalidOperationException("Expected the first worker claim.");
        var second = await store.TryClaimNextAsync("worker-two", Now.AddMinutes(6))
            ?? throw new InvalidOperationException("Expected the expired claim to be reclaimed.");

        Assert.NotEqual(first.LeaseToken, second.LeaseToken);
        Assert.Equal(first.LeaseVersion + 1, second.LeaseVersion);
        var resolved = SuccessfulResolution(workspace.Id, accepted.Instance.Id);
        var stale = CreateResolutionCommit(first, resolved, Now.AddMinutes(1));
        await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => store.CommitResolvedAsync(stale));

        var current = CreateResolutionCommit(second, resolved, Now.AddMinutes(7), "worker-two");
        var result = await store.CommitResolvedAsync(current);

        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, result.Outcome);
        Assert.Equal(1, await db.DeploymentRuns.CountAsync());
    }

    [Fact]
    public async Task Pending_delete_without_owned_resources_finalizes_locally_after_prior_operation_completes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Waiting delete workspace");
        var service = new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Worker Elsa", "waiting-delete-elsa", WorkerIntent(), "waiting-delete-create"));
        var (_, environmentId) = await AddManagedEnvironmentAsync(db, workspace, created.Instance.Id);
        var deletion = await service.DeleteAsync(new ElsaInstanceLifecycleRequest(
            workspace.Id, created.Instance.Id, created.Instance.Version, "waiting-delete"));
        await CompleteOperationAsync(db, created.Operation.Id);

        var result = await new ElsaInstanceDeletionWorker(
                new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
                    new FixedTimeProvider(Now.AddMinutes(1))),
                new ThrowingCleanupPort(), new FixedTimeProvider(Now.AddMinutes(1)))
            .ProcessAvailableAsync("deletion-worker");

        Assert.Equal(0, result.ProviderInvocations);
        var storedInstance = await db.ElsaInstances.SingleAsync(x => x.Id == deletion.Instance.Id);
        var storedOperation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == deletion.Operation.Id);
        Assert.Equal(ElsaObservedLifecycle.Deleted, storedInstance.ObservedLifecycle);
        Assert.Equal(Now.AddMinutes(1), storedInstance.DeletedAt);
        Assert.Equal(ElsaInstanceOperationState.Succeeded, storedOperation.State);
        Assert.Equal("deletion.local.absent", storedOperation.DeletionDiagnosticCode);
        Assert.Equal(64, storedOperation.DeletionEvidenceFingerprint!.Length);
        Assert.Null((await db.DeploymentEnvironments.SingleAsync(x => x.Id == environmentId)).ElsaInstanceId);
        Assert.Equal(1, await db.ElsaInstanceAuditEvents.CountAsync(x =>
            x.InstanceId == deletion.Instance.Id && x.EventType == "lifecycle.deleted"));

        db.ChangeTracker.Clear();
        var tombstone = await CreateStore(db).GetInstanceAsync(workspace.Id, deletion.Instance.Id);
        var repeated = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(2)))
            .DeleteAsync(new(workspace.Id, deletion.Instance.Id, tombstone!.Version, "delete-again"));
        var afterReplay = await new ElsaInstanceDeletionWorker(
                new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
                    new FixedTimeProvider(Now.AddMinutes(2))), new ThrowingCleanupPort(),
                new FixedTimeProvider(Now.AddMinutes(2)))
            .ProcessAvailableAsync("deletion-worker-replay");
        Assert.Equal(ElsaInstanceOperationState.Succeeded, repeated.Operation.State);
        Assert.Empty(afterReplay.Results);
        Assert.Equal(1, await db.ElsaInstanceAuditEvents.CountAsync(x =>
            x.InstanceId == deletion.Instance.Id && x.EventType == "lifecycle.deleted"));
    }

    [Fact]
    public async Task Ambiguous_provider_cleanup_remains_recovery_required_and_never_tombstones()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var (workspace, accepted) = await QueueManagedLifecycleRunAsync(db, "Ambiguous deletion workspace");
        await CompleteManagedRunAsync(db, accepted.Operation.Id, accepted.Instance.Id);
        var current = await CreateStore(db).GetInstanceAsync(workspace.Id, accepted.Instance.Id);
        var deletion = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(2)))
            .DeleteAsync(new(workspace.Id, accepted.Instance.Id, current!.Version, "ambiguous-delete"));
        var port = new QueueCleanupPort(new ElsaInstanceCleanupObservation(
            ElsaInstanceCleanupObservationKind.Ambiguous, deletion.Operation.Id,
            deletion.Operation.AttemptNumber, "deletion.provider.ambiguous"));

        var result = await new ElsaInstanceDeletionWorker(
                new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
                    new FixedTimeProvider(Now.AddMinutes(3))), port, new FixedTimeProvider(Now.AddMinutes(3)))
            .ProcessAvailableAsync("deletion-worker");

        Assert.Equal(1, result.ProviderInvocations);
        var storedInstance = await db.ElsaInstances.SingleAsync(x => x.Id == accepted.Instance.Id);
        var storedOperation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == deletion.Operation.Id);
        Assert.NotEqual(ElsaObservedLifecycle.Deleted, storedInstance.ObservedLifecycle);
        Assert.Null(storedInstance.DeletedAt);
        Assert.Equal(ElsaInstanceOperationState.RecoveryRequired, storedOperation.State);
        Assert.Equal("deletion.provider.ambiguous", storedOperation.DeletionDiagnosticCode);

    }

    [Fact]
    public async Task Unknown_instance_rejects_a_forged_local_deletion_proof_at_the_store_boundary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Unknown deletion proof workspace");
        var service = new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Unknown Elsa", "unknown-delete-elsa", WorkerIntent(), "unknown-create"));
        await CompleteOperationAsync(db, created.Operation.Id);
        var entity = await db.ElsaInstances.SingleAsync(x => x.Id == created.Instance.Id);
        entity.ObservedLifecycle = ElsaObservedLifecycle.Unknown;
        entity.Health = ElsaInstanceHealth.Unknown;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var current = await CreateStore(db).GetInstanceAsync(workspace.Id, created.Instance.Id);
        var deletion = await service.DeleteAsync(new(
            workspace.Id, created.Instance.Id, current!.Version, "unknown-delete"));
        var store = new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
            new FixedTimeProvider(Now.AddMinutes(1)));
        var item = await store.TryClaimNextDeletionAsync("deletion-worker", Now.AddMinutes(1));
        Assert.NotNull(item);
        Assert.False(item!.CanFinalizeLocally);
        var observation = new ElsaInstanceCleanupObservation(ElsaInstanceCleanupObservationKind.ConfirmedAbsent,
            deletion.Operation.Id, deletion.Operation.AttemptNumber, "deletion.local.absent");
        var commit = new ElsaInstanceDeletionCommit(workspace.Id, created.Instance.Id, deletion.Operation.Id,
            item.Outbox.Id, item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId,
            "deletion-worker", item.LeaseToken, item.LeaseVersion, observation.ComputeFingerprint(),
            ElsaInstanceDeletionProofKind.LocalNoOwnedResources, observation.DiagnosticCode, null, null,
            ElsaInstanceStateMachine.FinalizeDeletion(item.Instance, Now.AddMinutes(2)),
            item.Operation.TransitionTo(ElsaInstanceOperationState.Succeeded), Now.AddMinutes(2));

        await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => store.CommitDeletionAsync(commit));
        Assert.Equal(ElsaObservedLifecycle.Unknown,
            (await db.ElsaInstances.AsNoTracking().SingleAsync(x => x.Id == created.Instance.Id)).ObservedLifecycle);
    }

    [Fact]
    public async Task Confirmed_absence_finalization_replays_without_duplicate_audit_or_version_increment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var (workspace, accepted) = await QueueManagedLifecycleRunAsync(db, "Deletion replay workspace");
        await CompleteManagedRunAsync(db, accepted.Operation.Id, accepted.Instance.Id);
        var current = await CreateStore(db).GetInstanceAsync(workspace.Id, accepted.Instance.Id);
        var deletion = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(2)))
            .DeleteAsync(new(workspace.Id, accepted.Instance.Id, current!.Version, "replay-delete"));
        var store = new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
            new FixedTimeProvider(Now.AddMinutes(3)));
        var item = await store.TryClaimNextDeletionAsync("deletion-worker", Now.AddMinutes(3));
        Assert.NotNull(item);
        var deletedAt = Now.AddMinutes(4);
        var evidence = new ElsaInstanceCleanupEvidence("https://evidence.example/deletions/replay", Digest('e'));
        var observation = new ElsaInstanceCleanupObservation(ElsaInstanceCleanupObservationKind.ConfirmedAbsent,
            deletion.Operation.Id, deletion.Operation.AttemptNumber, "deletion.provider.absent", evidence);
        var commit = new ElsaInstanceDeletionCommit(workspace.Id, accepted.Instance.Id, deletion.Operation.Id,
            item!.Outbox.Id, item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId,
            "deletion-worker", item.LeaseToken, item.LeaseVersion, observation.ComputeFingerprint(),
            ElsaInstanceDeletionProofKind.ProviderConfirmedAbsent, observation.DiagnosticCode, evidence.Reference, evidence.Digest,
            ElsaInstanceStateMachine.FinalizeDeletion(item.Instance, deletedAt),
            item.Operation.TransitionTo(ElsaInstanceOperationState.Succeeded), deletedAt);

        var first = await store.CommitDeletionAsync(commit);
        var replay = await store.CommitDeletionAsync(commit);

        Assert.Equal(ElsaInstanceDeletionOutcome.Deleted, first.Outcome);
        Assert.Equal(ElsaInstanceDeletionOutcome.AlreadyCompleted, replay.Outcome);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Instance.Version, replay.Instance.Version);
        var run = await db.DeploymentRuns.SingleAsync(x => x.Id == item.CorrelatedRunId!.Value);
        var environment = await db.DeploymentEnvironments.SingleAsync(x => x.Id == run.EnvironmentId);
        Assert.Null(environment.ElsaInstanceId);
        Assert.Null(environment.DesiredRevisionId);
        Assert.Null(environment.DeployedRevisionId);
        Assert.Equal(1, await db.ElsaInstanceAuditEvents.CountAsync(x =>
            x.InstanceId == accepted.Instance.Id && x.EventType == "lifecycle.deleted"));
    }

    [Fact]
    public async Task Expired_deletion_claim_rotates_lease_without_provider_or_duplicate_operation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Deletion lease workspace");
        var service = new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Lease Elsa", "lease-elsa", WorkerIntent(), "lease-create"));
        var deletion = await service.DeleteAsync(new(workspace.Id, created.Instance.Id, created.Instance.Version, "lease-delete"));
        await CompleteOperationAsync(db, created.Operation.Id);
        var store = new EfCoreElsaInstanceLifecycleStore(db, EmptyResolutionInputSource.Instance,
            new FixedTimeProvider(Now.AddMinutes(6)));

        var first = await store.TryClaimNextDeletionAsync("worker-one", Now);
        var blocked = await store.TryClaimNextDeletionAsync("worker-two", Now.AddMinutes(4));
        var reclaimed = await store.TryClaimNextDeletionAsync("worker-two", Now.AddMinutes(6));

        Assert.NotNull(first);
        Assert.Null(blocked);
        Assert.NotNull(reclaimed);
        Assert.Equal(first!.Operation.Id, reclaimed!.Operation.Id);
        Assert.Equal(deletion.Operation.Id, reclaimed.Operation.Id);
        Assert.Equal(first.LeaseVersion + 1, reclaimed.LeaseVersion);
        Assert.NotEqual(first.LeaseToken, reclaimed.LeaseToken);
        Assert.Equal(1, await db.ElsaInstanceOperations.CountAsync(x => x.Id == deletion.Operation.Id));
    }

    [Fact]
    public async Task Stale_managed_run_marks_run_operation_and_instance_recovery_required_atomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var (workspace, accepted) = await QueueManagedLifecycleRunAsync(db, "Stale managed run");
        var workspaceStore = new DeploymentWorkspaceStore(db);
        var claimed = await workspaceStore.ClaimNextQueuedRunAsync("deployment-worker", Now);
        Assert.NotNull(claimed);

        var marked = await workspaceStore.MarkStaleRunningRunsRecoveryRequiredAsync(
            Now.AddMinutes(10), TimeSpan.FromMinutes(5));

        Assert.Equal(1, marked);
        db.ChangeTracker.Clear();
        var run = await db.DeploymentRuns.Include(x => x.Environment).SingleAsync(x => x.Id == claimed!.Id);
        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == accepted.Operation.Id);
        var instance = await db.ElsaInstances.SingleAsync(x => x.Id == accepted.Instance.Id);
        Assert.Equal(WorkspaceDeploymentRunStatus.RecoveryRequired, run.Status);
        Assert.Null(run.CompletedAt);
        Assert.Equal(ElsaInstanceOperationState.RecoveryRequired, operation.State);
        Assert.Null(operation.CompletedAt);
        Assert.Equal(ElsaObservedLifecycle.Unknown, instance.ObservedLifecycle);
        Assert.Equal(ElsaInstanceHealth.Unknown, instance.Health);
        Assert.Equal(DeploymentStatus.Blocked, run.Environment!.DeploymentStatus);
        var audit = await db.ElsaInstanceAuditEvents.SingleAsync(x => x.EventType == "lifecycle.recovery-required");
        Assert.Equal("provider.reconciliation.required", audit.DiagnosticCode);
        Assert.DoesNotContain("deployment-worker", audit.Summary ?? "", StringComparison.Ordinal);
        Assert.Equal(workspace.Id, audit.WorkspaceId);
    }

    [Fact]
    public async Task Recovery_resume_requeues_the_managed_deployment_run()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var (workspace, accepted) = await QueueManagedLifecycleRunAsync(db, "Recovery resume");
        var workspaceStore = new DeploymentWorkspaceStore(db);
        var claimed = await workspaceStore.ClaimNextQueuedRunAsync("deployment-worker", Now);
        Assert.NotNull(claimed);
        Assert.Equal(1, await workspaceStore.MarkStaleRunningRunsRecoveryRequiredAsync(
            Now.AddMinutes(10), TimeSpan.FromMinutes(5)));
        db.ChangeTracker.Clear();

        var lifecycleStore = CreateStore(db);
        var reconciliation = new ElsaInstanceProviderReconciliationService(
            lifecycleStore,
            new QueueProviderPort(new ElsaInstanceProviderObservation(
                ElsaInstanceProviderObservationKind.Ambiguous,
                ElsaObservedLifecycle.Unknown,
                ElsaInstanceProviderHealthGate.Unknown,
                "retry-safe-observation",
                new ElsaInstanceProviderRetryEvidence(
                    "https://evidence.example/retry/recovery-resume",
                    "sha256:" + new string('a', 64)))),
            new FixedTimeProvider(Now.AddMinutes(11)));
        var reconciled = await reconciliation.ReconcileAsync(workspace.Id, accepted.Operation.Id);
        Assert.True(reconciled.RetrySafe);

        var current = await lifecycleStore.GetInstanceAsync(workspace.Id, accepted.Instance.Id);
        Assert.NotNull(current);
        var recovered = await new ElsaInstanceLifecycleService(lifecycleStore, new FixedTimeProvider(Now.AddMinutes(12)))
            .RecoverAsync(new(workspace.Id, accepted.Instance.Id, current!.Version, "resume-recovery"));

        Assert.Equal(ElsaInstanceOperationState.Queued, recovered.Operation.State);
        db.ChangeTracker.Clear();
        var run = await db.DeploymentRuns.SingleAsync(x => x.Id == claimed!.Id);
        Assert.Equal(WorkspaceDeploymentRunStatus.Queued, run.Status);
        Assert.Null(run.WorkerId);
        Assert.Null(run.WorkerHeartbeatAt);
        Assert.Equal(DeploymentStatus.Running,
            (await db.DeploymentEnvironments.SingleAsync(x => x.Id == run.EnvironmentId)).DeploymentStatus);
        Assert.Contains(await db.DeploymentRunHistoryEvents.Where(x => x.RunId == run.Id).ToListAsync(),
            x => x.Status == WorkspaceDeploymentRunStatus.Queued &&
                 x.Message == "Deployment run requeued after provider reconciliation.");
        db.ChangeTracker.Clear();
        Assert.NotNull(await workspaceStore.ClaimNextQueuedRunAsync("recovery-worker", Now.AddMinutes(13)));
    }

    [Fact]
    public async Task Provider_reconciliation_persists_uncertainty_then_converges_and_replays()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var (_, accepted) = await QueueManagedLifecycleRunAsync(db, "Reconciliation persistence");
        var workspaceStore = new DeploymentWorkspaceStore(db);
        _ = await workspaceStore.ClaimNextQueuedRunAsync("deployment-worker", Now);
        Assert.Equal(1, await workspaceStore.MarkStaleRunningRunsRecoveryRequiredAsync(
            Now.AddMinutes(10), TimeSpan.FromMinutes(5)));
        db.ChangeTracker.Clear();

        var port = new QueueProviderPort(
            new(ElsaInstanceProviderObservationKind.Ambiguous, ElsaObservedLifecycle.Unknown,
                ElsaInstanceProviderHealthGate.Unknown, "provider-observation-1",
                new ElsaInstanceProviderRetryEvidence(
                    "https://evidence.example/retry/provider-observation-1",
                    "sha256:" + new string('b', 64))),
            new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                ElsaInstanceProviderHealthGate.Passed, "provider-observation-2"));
        var lifecycleStore = new EfCoreElsaInstanceLifecycleStore(
            db, EmptyResolutionInputSource.Instance, new FixedTimeProvider(Now.AddMinutes(11)));
        var service = new ElsaInstanceProviderReconciliationService(
            lifecycleStore, port, new FixedTimeProvider(Now.AddMinutes(11)));
        var originalTarget = await lifecycleStore.GetTargetAsync(
            accepted.Instance.WorkspaceId, accepted.Operation.Id)
            ?? throw new InvalidOperationException("Expected a reconciliation target.");

        var uncertain = await service.ReconcileAsync(accepted.Instance.WorkspaceId, accepted.Operation.Id);
        var converged = await service.ReconcileAsync(accepted.Instance.WorkspaceId, accepted.Operation.Id);
        var replay = await service.ReconcileAsync(accepted.Instance.WorkspaceId, accepted.Operation.Id);

        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.RecoveryRequired, uncertain.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Unknown, uncertain.Projection.ObservedLifecycle);
        Assert.Equal(ElsaInstanceProviderReconciliationOutcome.Converged, converged.Outcome);
        Assert.Equal(ElsaObservedLifecycle.Ready, converged.Projection.ObservedLifecycle);
        Assert.Equal(ElsaInstanceHealth.Healthy, converged.Projection.Health);
        Assert.True(replay.Replayed);
        Assert.Equal(2, port.Calls);
        db.ChangeTracker.Clear();
        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == accepted.Operation.Id);
        var run = await db.DeploymentRuns.SingleAsync(x => x.Id == operation.DeploymentRunId);
        Assert.Equal(ElsaInstanceOperationState.Succeeded, operation.State);
        Assert.Equal(WorkspaceDeploymentRunStatus.Succeeded, run.Status);
        Assert.Equal(Now.AddMinutes(11), operation.CompletedAt);
        var persistedInstanceVersion = await db.ElsaInstances
            .Where(x => x.Id == accepted.Instance.Id)
            .Select(x => x.Version)
            .SingleAsync();
        Assert.Equal(persistedInstanceVersion, converged.Projection.InstanceVersion);
        Assert.Equal(persistedInstanceVersion, operation.ReconciledInstanceVersion);
        Assert.Equal(2, await db.ElsaInstanceAuditEvents.CountAsync(x =>
            x.OperationId == accepted.Operation.Id && x.EventType == "lifecycle.reconciled"));
        Assert.Equal("https://evidence.example/retry/provider-observation-1",
            operation.ReconciliationRetryEvidenceReference);
        Assert.Equal("sha256:" + new string('b', 64), operation.ReconciliationRetryEvidenceDigest);
        Assert.False(replay.RetrySafe);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycleStore.CommitAsync(new(
            accepted.Instance.WorkspaceId,
            accepted.Instance.Id,
            accepted.Operation.Id,
            originalTarget.Instance.Version,
            originalTarget.Operation.AttemptNumber,
            originalTarget.ReconciliationVersion,
            new string('A', 64),
            originalTarget.Instance,
            originalTarget.Operation,
            ElsaInstanceProviderReconciliationService.ConvergedCode,
            false,
            null,
            null,
            Now.AddMinutes(12))));

        var concurrentReplay = await lifecycleStore.CommitAsync(new(
            accepted.Instance.WorkspaceId,
            accepted.Instance.Id,
            accepted.Operation.Id,
            originalTarget.Instance.Version,
            originalTarget.Operation.AttemptNumber,
            originalTarget.ReconciliationVersion,
            operation.ReconciliationEvidenceFingerprint!,
            originalTarget.Instance,
            originalTarget.Operation,
            ElsaInstanceProviderReconciliationService.ConvergedCode,
            false,
            null,
            null,
            Now.AddMinutes(12)));
        Assert.True(concurrentReplay.Replayed);
        Assert.False(concurrentReplay.RetrySafe);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ElsaInstances SET ObservedLifecycle = 'Failed', Health = 'Unreachable', Version = Version + 1 WHERE Id = {accepted.Instance.Id}");
        db.ChangeTracker.Clear();
        var historicalReplay = await service.ReconcileAsync(accepted.Instance.WorkspaceId, accepted.Operation.Id);
        Assert.Equal(ElsaObservedLifecycle.Ready, historicalReplay.Projection.ObservedLifecycle);
        Assert.Equal(ElsaInstanceHealth.Healthy, historicalReplay.Projection.Health);
        Assert.Equal(converged.Projection.InstanceVersion, historicalReplay.Projection.InstanceVersion);

        await Assert.ThrowsAsync<ElsaInstanceLifecycleConflictException>(() => lifecycleStore.CommitAsync(new(
            accepted.Instance.WorkspaceId,
            accepted.Instance.Id,
            accepted.Operation.Id,
            originalTarget.Instance.Version,
            originalTarget.Operation.AttemptNumber,
            originalTarget.ReconciliationVersion,
            new string('c', 64),
            originalTarget.Instance,
            originalTarget.Operation,
            ElsaInstanceProviderReconciliationService.AmbiguousCode,
            false,
            null,
            null,
            Now.AddMinutes(12))));
    }

    [Fact]
    public async Task Malformed_first_persisted_instance_is_quarantined_and_later_valid_work_continues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Malformed work workspace");
        var first = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Malformed Elsa", "malformed-worker-elsa", WorkerIntent(), "malformed-create"));
        var second = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(1)))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Valid Elsa", "valid-worker-elsa", WorkerIntent(), "valid-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, second.Instance.Id);

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ElsaInstances SET FeatureOverridesJson = 'not-json' WHERE Id = {first.Instance.Id}");
        db.ChangeTracker.Clear();
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(second.Instance, target),
            new FixedTimeProvider(Now.AddMinutes(2)));
        var result = await new ElsaInstanceLifecycleWorker(
                store,
                new StaticResolver(SuccessfulResolution(workspace.Id, second.Instance.Id)),
                new FixedTimeProvider(Now.AddMinutes(2)))
            .ProcessAvailableAsync("worker-one");

        Assert.Single(result.Results);
        Assert.Equal(second.Operation.Id, result.Results[0].Operation.Id);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, result.Results[0].Outcome);
        Assert.Equal(ElsaInstanceOperationState.Failed,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == first.Operation.Id)).State);
        Assert.Equal("outbox.invalid",
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == first.Operation.Id)).FailureCode);
        Assert.Equal(ElsaInstanceOperationState.Queued,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == second.Operation.Id)).State);
    }

    [Fact]
    public async Task Untrusted_first_envelope_is_skipped_and_later_valid_work_continues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Untrusted work workspace");
        var first = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Untrusted Elsa", "untrusted-worker-elsa", WorkerIntent(), "untrusted-create"));
        var second = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(1)))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Valid Elsa", "valid-after-untrusted-elsa", WorkerIntent(), "valid-after-untrusted-create"));
        var target = await AddManagedEnvironmentAsync(db, workspace, second.Instance.Id);

        // Bypass the application boundary to model a row whose operation cannot
        // be safely reconstructed. It must not be quarantined or starve later work.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ElsaInstanceOperations SET IdempotencyKey = '' WHERE Id = {first.Operation.Id}");
        db.ChangeTracker.Clear();
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(second.Instance, target),
            new FixedTimeProvider(Now.AddMinutes(2)));
        var result = await new ElsaInstanceLifecycleWorker(
                store,
                new StaticResolver(SuccessfulResolution(workspace.Id, second.Instance.Id)),
                new FixedTimeProvider(Now.AddMinutes(2)))
            .ProcessAvailableAsync("worker-one");

        Assert.Single(result.Results);
        Assert.Equal(second.Operation.Id, result.Results[0].Operation.Id);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, result.Results[0].Outcome);
        Assert.Equal(ElsaInstanceOperationState.Accepted,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == first.Operation.Id)).State);
        Assert.Equal(ElsaInstanceOperationState.Queued,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == second.Operation.Id)).State);
        var quarantined = await db.ElsaInstanceLifecycleOutbox
            .SingleAsync(x => x.OperationId == first.Operation.Id);
        Assert.Equal("outbox.invalid", quarantined.QuarantineCode);
        Assert.Equal(Now.AddMinutes(2), quarantined.QuarantinedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Corrupt_lease_version_is_quarantined_without_blocking_later_work(int leaseVersion)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, $"Corrupt lease workspace {leaseVersion}");
        var first = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Corrupt Elsa", $"corrupt-lease-{leaseVersion}", WorkerIntent(), $"corrupt-lease-create-{leaseVersion}"));
        var second = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now.AddMinutes(1)))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Valid Elsa", $"valid-after-lease-{leaseVersion}", WorkerIntent(), $"valid-after-lease-create-{leaseVersion}"));
        var target = await AddManagedEnvironmentAsync(db, workspace, second.Instance.Id);

        // Disable only the SQLite compatibility trigger to model a legacy row
        // that predates the database range guard.
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_LeaseVersion_Range_Insert;");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_LeaseVersion_Range_Update;");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ElsaInstanceOperations SET LeaseVersion = {leaseVersion} WHERE Id = {first.Operation.Id}");
        db.ChangeTracker.Clear();
        var store = new EfCoreElsaInstanceLifecycleStore(
            db,
            new StaticResolutionInputSource(second.Instance, target),
            new FixedTimeProvider(Now.AddMinutes(2)));
        var result = await new ElsaInstanceLifecycleWorker(
                store,
                new StaticResolver(SuccessfulResolution(workspace.Id, second.Instance.Id)),
                new FixedTimeProvider(Now.AddMinutes(2)))
            .ProcessAvailableAsync("worker-one");

        Assert.Single(result.Results);
        Assert.Equal(second.Operation.Id, result.Results[0].Operation.Id);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, result.Results[0].Outcome);
        Assert.Equal(ElsaInstanceOperationState.Accepted,
            (await db.ElsaInstanceOperations.SingleAsync(x => x.Id == first.Operation.Id)).State);
        var quarantined = await db.ElsaInstanceLifecycleOutbox
            .SingleAsync(x => x.OperationId == first.Operation.Id);
        Assert.Equal("outbox.invalid", quarantined.QuarantineCode);
        Assert.Equal(Now.AddMinutes(2), quarantined.QuarantinedAt);
    }

    private static async Task<Workspace> CreateWorkspaceAsync(CatalogDbContext db, string name)
    {
        var workspace = new Workspace { Name = name };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        return workspace;
    }

    private static EfCoreElsaInstanceLifecycleStore CreateStore(CatalogDbContext db) =>
        new(db, EmptyResolutionInputSource.Instance);

    private static async Task<(Workspace Workspace, ElsaInstanceLifecycleAcceptance Accepted)>
        QueueManagedLifecycleRunAsync(CatalogDbContext db, string workspaceName)
    {
        var workspace = await CreateWorkspaceAsync(db, workspaceName);
        var accepted = await new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now))
            .CreateAsync(new ElsaInstanceCreateRequest(
                workspace.OrganizationId, workspace.Id, "Managed Elsa", $"managed-{Guid.NewGuid():N}",
                WorkerIntent(), $"create-{Guid.NewGuid():N}"));
        var targetIds = await AddManagedEnvironmentAsync(db, workspace, accepted.Instance.Id);
        db.ChangeTracker.Clear();
        var store = new EfCoreElsaInstanceLifecycleStore(
            db, new StaticResolutionInputSource(accepted.Instance, targetIds), new FixedTimeProvider(Now));
        var result = await new ElsaInstanceLifecycleWorker(
            store, new StaticResolver(SuccessfulResolution(workspace.Id, accepted.Instance.Id)),
            new FixedTimeProvider(Now)).ProcessAvailableAsync("resolver-worker");
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Queued, Assert.Single(result.Results).Outcome);
        return (workspace, accepted);
    }

    private static async Task CompleteOperationAsync(CatalogDbContext db, Guid operationId)
    {
        db.ChangeTracker.Clear();
        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == operationId);
        operation.State = ElsaInstanceOperationState.Succeeded;
        await db.SaveChangesAsync();
    }

    private static async Task CompleteManagedRunAsync(CatalogDbContext db, Guid operationId, Guid instanceId)
    {
        db.ChangeTracker.Clear();
        var operation = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == operationId);
        operation.State = ElsaInstanceOperationState.Running;
        await db.SaveChangesAsync();
        operation.State = ElsaInstanceOperationState.Succeeded;
        operation.CompletedAt = Now.AddMinutes(1);
        var run = await db.DeploymentRuns.SingleAsync(x => x.ElsaInstanceId == instanceId);
        run.Status = WorkspaceDeploymentRunStatus.Succeeded;
        run.CompletedAt = Now.AddMinutes(1);
        var instance = await db.ElsaInstances.SingleAsync(x => x.Id == instanceId);
        instance.CurrentDeploymentId = "deployment-safe";
        instance.PlacementAssignmentId = "placement-safe";
        instance.ObservedLifecycle = ElsaObservedLifecycle.Ready;
        instance.Health = ElsaInstanceHealth.Healthy;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
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

    private static string Digest(char value) => "sha256:" + new string(value, 64);

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

    private static ElsaInstanceIntent WorkerIntent() => new(
        new ElsaReleaseIntent("future-runtime", "5.0", "5.0.0-preview.1", "preview"),
        new ElsaApplicationIntent("server-studio"),
        new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed"));

    private static async Task<(Guid ApplicationId, Guid EnvironmentId)> AddManagedEnvironmentAsync(
        CatalogDbContext db,
        Workspace workspace,
        Guid instanceId)
    {
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.DeploymentApplications.Add(new DeploymentApplicationEntity
        {
            Id = applicationId,
            WorkspaceId = workspace.Id,
            Name = $"Application-{instanceId:N}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DeploymentEnvironments.Add(new DeploymentEnvironmentEntity
        {
            Id = environmentId,
            WorkspaceId = workspace.Id,
            ApplicationId = applicationId,
            ElsaInstanceId = instanceId,
            Name = "Production",
            Tier = EnvironmentTier.Production,
            DeploymentStatus = DeploymentStatus.Blocked,
            DriftStatus = DriftStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (applicationId, environmentId);
    }

    private static ElsaInstanceLifecycleResolutionInput ResolutionInput(
        ElsaInstance instance,
        (Guid ApplicationId, Guid EnvironmentId) target) => new(
        new ElsaInstancePlanResolutionRequest(
            instance.Intent,
            new(new("future-runtime", null, null, null), [], [], [], null),
            AdmittedManifest(),
            "plan_worker_01",
            $"https://control.example.test/api/workspaces/{instance.WorkspaceId:D}/instances/{instance.Id:D}/resolved-plans/plan_worker_01",
            instance.WorkspaceId),
        new(target.ApplicationId, target.EnvironmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    private static ReleaseManifestAdmissionResult AdmittedManifest() =>
        new(
            true,
            "https://example.test/manifests/5.0.0-preview.1.json",
            "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            new(
                "1",
                new("future-runtime", "commercial", "5.0", "5.0.0-preview.1", "preview", "preview",
                    new("https://example.test/repo", "0123456789abcdef0123456789abcdef01234567", "build", "1")),
                [new(
                    "server-studio",
                    ["elsa.server"],
                    [new("paid", "registry.example.test/elsa/server@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")],
                    new Dictionary<string, string> { ["server"] = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
                    new Dictionary<string, string>(),
                    new("1", []),
                    new(null, null, [], null))]),
            new("https://example.test/signatures/5.0.0-preview.1.sig", "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"),
            "paid",
            "server-studio",
            []);

    private static ElsaInstanceLifecycleResolutionCommit CreateResolutionCommit(
        ElsaInstanceLifecycleWorkItem item,
        ElsaInstancePlanResolutionResult resolved,
        DateTimeOffset committedAt,
        string workerId = "worker-one")
    {
        var queued = item.Operation.TransitionTo(ElsaInstanceOperationState.Queued);
        var resolvedInstance = item.Instance.AttachResolvedPlan(resolved.Reference!, resolved.CurrentResolvedRelease!);
        return new ElsaInstanceLifecycleResolutionCommit(
            item.Outbox.WorkspaceId,
            item.Outbox.InstanceId,
            item.Operation.Id,
            item.Outbox.Id,
            item.Outbox.RequestHash,
            workerId,
            queued,
            resolvedInstance,
            new ElsaInstanceLifecycleResolvedPlan(
                resolved.Reference!,
                ResolvedElsaApplicationPlanSerialization.Serialize(resolved.Plan!)),
            item.Resolution.DeploymentTarget,
            committedAt,
            item.LeaseToken,
            item.LeaseVersion);
    }

    private static ElsaInstancePlanResolutionResult SuccessfulResolution(Guid workspaceId, Guid instanceId)
    {
        var provisionalReference = new ElsaResolvedPlanReference(
            "plan_worker_01",
            1,
            "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            $"https://control.example.test/api/workspaces/{workspaceId:D}/instances/{instanceId:D}/resolved-plans/plan_worker_01");
        var plan = new ResolvedElsaApplicationPlan(
            "1",
            new("future-runtime", "5.0", "5.0.0-preview.1", "https://example.test/repo", "0123456789abcdef0123456789abcdef01234567", "https://example.test/manifests/5.0.0-preview.1.json", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"),
            new("server-studio", [new("server", ["server"], new("paid", "registry.example.test/elsa", "registry.example.test/elsa/server@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), ["elsa.server"], [], [])]),
            [],
            new([]),
            new([], []),
            new("public", "public", false, [], []),
            "dedicated",
            new("preview", "preview", "stable", "automatic-within-minor", "explicit-approval", "explicit-migration"),
            [],
            [new("release-manifest", "https://example.test/manifests/5.0.0-preview.1.json", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "Verified release manifest.")]);
        var reference = new ElsaResolvedPlanReference(
            provisionalReference.PlanId,
            provisionalReference.SchemaVersion,
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(plan),
            provisionalReference.PlanUri);
        return new(
            true,
            plan,
            reference,
            new(reference, "future-runtime", "5.0", "5.0.0-preview.1", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", [new("server", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]),
            []);
    }

    private sealed class StaticResolutionInputSource(
        ElsaInstance instance,
        (Guid ApplicationId, Guid EnvironmentId) target) : IElsaInstanceLifecycleResolutionInputSource
    {
        public Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
            ElsaInstance requestedInstance,
            ElsaInstanceOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ElsaInstanceLifecycleResolutionInput?>(ResolutionInput(instance, target));
    }

    private sealed class EmptyResolutionInputSource : IElsaInstanceLifecycleResolutionInputSource
    {
        public static EmptyResolutionInputSource Instance { get; } = new();

        public Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
            ElsaInstance instance,
            ElsaInstanceOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ElsaInstanceLifecycleResolutionInput?>(null);
    }

    private sealed class StaticResolver(ElsaInstancePlanResolutionResult result) : IElsaInstancePlanResolver
    {
        public Task<ElsaInstancePlanResolutionResult> ResolveAsync(
            ElsaInstancePlanResolutionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class QueueProviderPort(params ElsaInstanceProviderObservation[] observations)
        : IElsaInstanceProviderReconciliationPort
    {
        private readonly Queue<ElsaInstanceProviderObservation> _observations = new(observations);

        public int Calls { get; private set; }

        public Task<ElsaInstanceProviderObservation> ObserveAsync(
            ElsaInstanceProviderReconciliationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_observations.Dequeue().Correlate(request));
        }
    }

    private sealed class ThrowingCleanupPort : IElsaInstanceProviderCleanupPort
    {
        public Task<ElsaInstanceCleanupObservation> CleanupAsync(
            ElsaInstanceCleanupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider cleanup must not run for a local tombstone.");
    }

    private sealed class QueueCleanupPort(params ElsaInstanceCleanupObservation[] observations)
        : IElsaInstanceProviderCleanupPort
    {
        private readonly Queue<ElsaInstanceCleanupObservation> _observations = new(observations);

        public Task<ElsaInstanceCleanupObservation> CleanupAsync(
            ElsaInstanceCleanupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_observations.Dequeue());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CatalogDbContext CreateMigratedContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        return new CatalogDbContext(options);
    }

    private sealed record Captured<T>(T? Value, Exception? Error);
}
