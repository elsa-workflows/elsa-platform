using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class ElsaInstanceLifecycleStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T10:00:00Z");

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
        var store = new EfCoreElsaInstanceLifecycleStore(db, source);
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
        var store = new EfCoreElsaInstanceLifecycleStore(db, new StaticResolutionInputSource(accepted.Instance, target));
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

    private static async Task<Workspace> CreateWorkspaceAsync(CatalogDbContext db, string name)
    {
        var workspace = new Workspace { Name = name };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        return workspace;
    }

    private static EfCoreElsaInstanceLifecycleStore CreateStore(CatalogDbContext db) =>
        new(db, EmptyResolutionInputSource.Instance);

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
        DateTimeOffset committedAt)
    {
        var queued = item.Operation.TransitionTo(ElsaInstanceOperationState.Queued);
        var resolvedInstance = item.Instance.AttachResolvedPlan(resolved.Reference!, resolved.CurrentResolvedRelease!);
        return new ElsaInstanceLifecycleResolutionCommit(
            item.Outbox.WorkspaceId,
            item.Outbox.InstanceId,
            item.Operation.Id,
            item.Outbox.Id,
            item.Outbox.RequestHash,
            "worker-one",
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
