using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed partial class ElsaInstanceLifecycleStoreTests
{
    [PosixFact]
    public async Task Delete_recovery_accepts_stale_cleanup_verified_boundary_without_runner_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        await using var fixture = await SeedDeleteRecoveryClaimAsync(db);

        await PrepareLateFinalizationRecoveryAsync(db, fixture, staleProviderLease: true);
        var beforeCommands = await fixture.Tools.ReadLogAsync();
        var (worker, runner) = CreateLateFinalizationDeletionWorker(db, fixture);

        var batch = await worker.ProcessAvailableAsync(fixture.Request.WorkerId);

        var result = Assert.Single(batch.Results);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Deleted, result.Outcome);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(beforeCommands, await fixture.Tools.ReadLogAsync());
        await AssertLateFinalizationCompletedAsync(db, fixture);
    }

    [PosixFact]
    public async Task Delete_recovery_accepts_explicit_uncertain_cleanup_verified_boundary_without_runner_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        await using var fixture = await SeedDeleteRecoveryClaimAsync(db);

        await PrepareLateFinalizationRecoveryAsync(db, fixture, staleProviderLease: false);
        var beforeCommands = await fixture.Tools.ReadLogAsync();
        var (worker, runner) = CreateLateFinalizationDeletionWorker(db, fixture);

        var batch = await worker.ProcessAvailableAsync(fixture.Request.WorkerId);

        var result = Assert.Single(batch.Results);
        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Deleted, result.Outcome);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(beforeCommands, await fixture.Tools.ReadLogAsync());
        await AssertLateFinalizationCompletedAsync(db, fixture);
    }

    [PosixFact]
    public async Task Delete_finalization_recovery_rejects_inventory_changed_after_acceptance_without_claim_or_runner_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        await using var fixture = await SeedDeleteRecoveryClaimAsync(db);
        await PrepareLateFinalizationRecoveryAsync(db, fixture, staleProviderLease: false);

        db.ChangeTracker.Clear();
        var assignment = await db.AzureProviderResourceAssignments.SingleAsync(
            x => x.WorkspaceId == fixture.WorkspaceId && x.InstanceId == fixture.InstanceId);
        assignment.WorkloadRevisionName = "remaining-revision";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await fixture.OperationStore.GetAsync(fixture.WorkspaceId, fixture.ProviderOperationId);
        var beforeCommands = await fixture.Tools.ReadLogAsync();
        var (worker, runner) = CreateLateFinalizationDeletionWorker(db, fixture);

        var batch = await worker.ProcessAvailableAsync(fixture.Request.WorkerId);

        Assert.Equal(ElsaInstanceLifecycleWorkerOutcome.Failed, Assert.Single(batch.Results).Outcome);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(beforeCommands, await fixture.Tools.ReadLogAsync());
        AssertClaimDidNotMutate(before,
            await fixture.OperationStore.GetAsync(fixture.WorkspaceId, fixture.ProviderOperationId));
        var lifecycle = await db.ElsaInstanceOperations.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Request.LifecycleOperationId);
        Assert.Equal(ElsaInstanceOperationState.RecoveryRequired, lifecycle.State);
        var retained = await db.AzureProviderResourceAssignments.AsNoTracking()
            .SingleAsync(x => x.Id == assignment.Id);
        Assert.Equal("remaining-revision", retained.WorkloadRevisionName);
        Assert.Equal(AzureProviderAssignmentState.Unknown, retained.State);
    }

    private static async Task PrepareLateFinalizationRecoveryAsync(
        CatalogDbContext db,
        DeleteClaimFixture fixture,
        bool staleProviderLease)
    {
        var claimed = Assert.IsType<AzureProviderOperation>(await fixture.Store.ClaimDeleteRecoveryAsync(
            fixture.Request, TimeSpan.FromMinutes(5), fixture.Now));
        var verified = Assert.IsType<AzureProviderOperation>(await fixture.OperationStore.CheckpointAsync(
            fixture.WorkspaceId,
            fixture.ProviderOperationId,
            fixture.Request.LeaseToken,
            new(
                AzureProviderOperationPhase.CleanupVerified,
                "cleanup.verified",
                "The provider cleanup postcondition was observed.",
                new(),
                null,
                AzureProviderHealth.Unknown,
                [],
                ReplaceResources: true),
            fixture.Now.AddSeconds(1),
            claimed.Version));

        var transitionAt = fixture.Now.AddMinutes(staleProviderLease ? 6 : 2);
        if (staleProviderLease)
        {
            Assert.Equal(1, await fixture.OperationStore.RecoverStaleAsync(transitionAt));
        }
        else
        {
            Assert.NotNull(await fixture.OperationStore.FinalizeAsync(
                fixture.WorkspaceId,
                fixture.ProviderOperationId,
                fixture.Request.LeaseToken,
                AzureProviderOperationStatus.RecoveryRequired,
                "cleanup.uncertain",
                transitionAt,
                verified.Version));
        }

        var lifecycleStore = new EfCoreElsaInstanceLifecycleStore(
            db, EmptyResolutionInputSource.Instance, new FixedTimeProvider(transitionAt));
        var uncertain = Assert.IsType<AzureProviderOperation>(await fixture.OperationStore.GetAsync(
            fixture.WorkspaceId, fixture.ProviderOperationId));
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, uncertain.Status);
        Assert.Equal(AzureProviderOperationPhase.CleanupVerified, uncertain.Phase);
        Assert.Null(uncertain.AttemptedStep);
        var retainedAssignment = Assert.IsType<AzureProviderResourceAssignment>(await
            ((IAzureProviderResourceAssignmentStore)fixture.OperationStore).GetAsync(
                fixture.WorkspaceId, uncertain.ProviderAssignmentId!.Value));
        Assert.Equal(staleProviderLease ? AzureProviderAssignmentState.Deleted : AzureProviderAssignmentState.Unknown,
            retainedAssignment.State);
        var reclaimed = staleProviderLease
            ? await lifecycleStore.TryClaimNextDeletionAsync(fixture.Request.WorkerId, transitionAt)
            : null;
        if (staleProviderLease)
            Assert.NotNull(reclaimed);
        var outbox = await db.ElsaInstanceLifecycleOutbox.AsNoTracking()
            .SingleAsync(x => x.OperationId == fixture.Request.LifecycleOperationId);
        await lifecycleStore.RequireDeletionRecoveryAsync(new(
            fixture.WorkspaceId,
            fixture.InstanceId,
            fixture.Request.LifecycleOperationId,
            outbox.Id,
            fixture.Request.InstanceVersion,
            fixture.Request.LifecycleAttemptNumber,
            null,
            fixture.Request.WorkerId,
            reclaimed?.LeaseToken ?? fixture.Request.LeaseToken,
            reclaimed?.LeaseVersion ?? fixture.Request.LeaseVersion,
            new string('f', 64),
            "deletion.provider-finalization-pending",
            transitionAt));

        var current = Assert.IsType<ElsaInstance>(await CreateStore(db).GetInstanceAsync(
            fixture.WorkspaceId, fixture.InstanceId));
        var accepted = await new ElsaInstanceLifecycleService(
                CreateStore(db), new FixedTimeProvider(transitionAt.AddMinutes(1)))
            .RecoverAsync(new(
                fixture.WorkspaceId,
                fixture.InstanceId,
                current.Version,
                staleProviderLease ? "late-finalization-stale" : "late-finalization-uncertain"));
        Assert.Equal(ElsaInstanceOperationState.Queued, accepted.Operation.State);
        Assert.Equal(fixture.Request.LifecycleOperationId, accepted.Operation.Id);
    }

    private static (ElsaInstanceDeletionWorker Worker, RejectLateFinalizationRunner Runner) CreateLateFinalizationDeletionWorker(
        CatalogDbContext db,
        DeleteClaimFixture fixture)
    {
        var scope = new AzureProviderTargetScope(
            "11111111-1111-1111-1111-111111111111",
            "rg-delete-claim",
            "22222222-2222-2222-2222-222222222222",
            "registry-rg",
            "registry1",
            "westeurope");
        var runnerOptions = fixture.Tools.Options;
        var providerOptions = new AzureElsaInstanceProviderOptions
        {
            Enabled = true,
            TemplateFingerprint = runnerOptions.ComputeTemplateAuthorityFingerprint(),
            ProviderScopeFingerprint = runnerOptions.ComputeProviderScopeFingerprint(scope),
            SubscriptionId = scope.SubscriptionId,
            ResourceGroupNamePrefix = scope.ResourceGroupName
        };
        var operationStore = new AzureProviderOperationStore(db);
        var runner = new RejectLateFinalizationRunner();
        var provider = CreateProvider(operationStore, providerOptions, fixture.Now.AddMinutes(8));
        var recoveryProvider = CreateProvider(
            operationStore,
            providerOptions,
            fixture.Now.AddMinutes(8),
            new AzureProviderExecutor(operationStore, runner,
                new FixedTimeProvider(fixture.Now.AddMinutes(8)), assignmentStore: operationStore));
        return (new(
            new EfCoreElsaInstanceLifecycleStore(
                db, EmptyResolutionInputSource.Instance, new FixedTimeProvider(fixture.Now.AddMinutes(8))),
            provider,
            new FixedTimeProvider(fixture.Now.AddMinutes(8)),
            recoveryProvider), runner);
    }

    private sealed class RejectLateFinalizationRunner : IAzureProviderRunner
    {
        public int Calls { get; private set; }

        public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Verified cleanup must not invoke the provider runner.");
        }
    }

    private static async Task AssertLateFinalizationCompletedAsync(
        CatalogDbContext db,
        DeleteClaimFixture fixture)
    {
        db.ChangeTracker.Clear();
        var lifecycle = await db.ElsaInstanceOperations.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Request.LifecycleOperationId);
        Assert.Equal(ElsaInstanceOperationState.Succeeded, lifecycle.State);
        var instance = await db.ElsaInstances.AsNoTracking().SingleAsync(x => x.Id == fixture.InstanceId);
        Assert.Equal(ElsaObservedLifecycle.Deleted, instance.ObservedLifecycle);
        var provider = await db.AzureProviderOperations.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.ProviderOperationId);
        Assert.Equal(AzureProviderOperationPhase.CleanupVerified, provider.Phase);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, provider.Status);
        Assert.Null(provider.AttemptedStep);
        var assignment = Assert.IsType<AzureProviderResourceAssignment>(await
            ((IAzureProviderResourceAssignmentStore)new AzureProviderOperationStore(db))
                .GetAsync(fixture.WorkspaceId, provider.ProviderAssignmentId!.Value));
        Assert.Equal(AzureProviderAssignmentState.Deleted, assignment.State);
        Assert.Equal(new AzureProviderResourceReferences(assignment.ResourceGroupName), assignment.Resources);
        Assert.Equal(fixture.ProviderOperationId, assignment.LastOperationId);
        Assert.Equal(1, await db.AzureProviderOperations.AsNoTracking()
            .CountAsync(x => x.WorkspaceId == fixture.WorkspaceId && x.InstanceId == fixture.InstanceId &&
                             x.Action == AzureProviderOperationAction.Delete));
    }
}
