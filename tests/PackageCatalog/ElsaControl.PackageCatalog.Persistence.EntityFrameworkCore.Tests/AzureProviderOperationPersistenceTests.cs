using ElsaControl.Deployment.Azure;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class AzureProviderOperationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _workspaceId = Guid.NewGuid();

    public AzureProviderOperationPersistenceTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        db.Workspaces.Add(new Workspace { Id = _workspaceId, Name = "Azure operation workspace" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Create_is_idempotent_and_transitions_are_append_only()
    {
        var request = Request();
        using var db = CreateContext();
        var store = new AzureProviderOperationStore(db);
        var first = await store.CreateOrGetAsync(request, DateTimeOffset.UtcNow);
        var second = await store.CreateOrGetAsync(request, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(first.Id, second.Id);
        var accepted = Assert.Single(await store.ListTransitionsAsync(_workspaceId, first.Id));
        Assert.Equal("Azure provider operation accepted.", accepted.Message);
    }

    [Fact]
    public async Task Unknown_workspace_is_rejected_by_catalog_foreign_key()
    {
        using var db = CreateContext();
        var request = Request() with { WorkspaceId = Guid.NewGuid() };
        await Assert.ThrowsAsync<DbUpdateException>(() => new AzureProviderOperationStore(db).CreateOrGetAsync(request, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Equivalent_active_identity_converges_even_with_a_new_request_key()
    {
        using var db = CreateContext();
        var store = new AzureProviderOperationStore(db);
        var first = await store.CreateOrGetAsync(Request(), DateTimeOffset.UtcNow);
        var second = await store.CreateOrGetAsync(Request() with { IdempotencyKey = "replayed-request" }, DateTimeOffset.UtcNow);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Claim_checkpoint_finalize_and_stale_recovery_preserve_history()
    {
        var now = DateTimeOffset.UtcNow;
        using var db = CreateContext();
        var store = new AzureProviderOperationStore(db);
        var operation = await store.CreateOrGetAsync(Request(), now);
        var claimed = await store.ClaimAsync(_workspaceId, operation.Id, "worker-1", "lease-1", TimeSpan.FromMinutes(1), now);
        Assert.NotNull(claimed);
        Assert.Null(await store.ClaimAsync(_workspaceId, operation.Id, "worker-2", "lease-2", TimeSpan.FromMinutes(1), now));

        var checkpoint = await store.CheckpointAsync(_workspaceId, operation.Id, "lease-1", new(
            AzureProviderOperationPhase.FoundationReady, "foundation.ready", "hunter2",
            new(ResourceGroupName: "rg-safe", FoundationDeploymentId: "deployment-1"), null, AzureProviderHealth.Unknown,
            [new("foundation.note", "even-more-sensitive")]), now);
        var savedCheckpoint = Assert.IsType<AzureProviderOperation>(checkpoint);
        Assert.Equal(AzureProviderOperationPhase.FoundationReady, savedCheckpoint.Phase);
        Assert.Equal("foundation.note", Assert.Single(savedCheckpoint.Diagnostics).Message);
        var replay = await store.CheckpointAsync(_workspaceId, operation.Id, "lease-1", new(
            AzureProviderOperationPhase.FoundationReady, "foundation.ready", "hunter2",
            new(ResourceGroupName: "rg-safe", FoundationDeploymentId: "deployment-1"), null, AzureProviderHealth.Unknown,
            [new("foundation.note", "different-unpersisted-detail")]), now);
        Assert.Equal(savedCheckpoint.Version, replay?.Version);
        var completed = await store.FinalizeAsync(_workspaceId, operation.Id, "lease-1", AzureProviderOperationStatus.Succeeded, "operation.succeeded", now, savedCheckpoint.Version);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, completed?.Status);
        Assert.Null(await store.FinalizeAsync(_workspaceId, operation.Id, "wrong-lease", AzureProviderOperationStatus.Succeeded, "operation.succeeded", now));
        Assert.Equal(completed?.Id, (await store.FinalizeAsync(_workspaceId, operation.Id, "lease-1", AzureProviderOperationStatus.Succeeded, "operation.succeeded", now, savedCheckpoint.Version))?.Id);
        Assert.Null(await store.FinalizeAsync(_workspaceId, operation.Id, "lease-1", AzureProviderOperationStatus.Succeeded, "operation.different", now, savedCheckpoint.Version));
        Assert.Equal(4, (await store.ListTransitionsAsync(_workspaceId, operation.Id)).Count);
        Assert.DoesNotContain(await store.ListTransitionsAsync(_workspaceId, operation.Id), x => x.Message.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_lease_requires_recovery_and_rejects_old_lease()
    {
        var now = DateTimeOffset.UtcNow;
        using var db = CreateContext();
        var store = new AzureProviderOperationStore(db);
        var operation = await store.CreateOrGetAsync(Request(), now);
        await store.ClaimAsync(_workspaceId, operation.Id, "worker-1", "lease-1", TimeSpan.FromMinutes(1), now);
        Assert.Equal(1, await store.RecoverStaleAsync(now.AddMinutes(2)));
        var recovered = await store.GetAsync(_workspaceId, operation.Id);
        Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, recovered?.Status);
        Assert.Null(await store.ClaimAsync(_workspaceId, operation.Id, "worker-2", "lease-2", TimeSpan.FromMinutes(1), now.AddMinutes(2)));
        Assert.NotNull(await store.ClaimRecoveryAsync(_workspaceId, operation.Id, "worker-2", "lease-2", TimeSpan.FromMinutes(1), now.AddMinutes(2), recovered!.Version));
        Assert.Null(await store.CheckpointAsync(_workspaceId, operation.Id, "lease-1", new(
            AzureProviderOperationPhase.FoundationReady, "late", "Late.", new(), null, AzureProviderHealth.Unknown, []), now.AddMinutes(2)));
        Assert.Null(recovered?.CompletedAt);
    }

    [Fact]
    public async Task Concurrent_claim_has_one_winner()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-azure-operation-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite($"Data Source={path}").Options;
            await using (var seed = new CatalogDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Workspaces.Add(new Workspace { Id = _workspaceId, Name = "Azure operation workspace" });
                await seed.SaveChangesAsync();
                var operation = await new AzureProviderOperationStore(seed).CreateOrGetAsync(Request(), DateTimeOffset.UtcNow);
                await using var first = new CatalogDbContext(options);
                await using var second = new CatalogDbContext(options);
                var firstClaim = new AzureProviderOperationStore(first).ClaimAsync(_workspaceId, operation.Id, "worker-a", "lease-a", TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);
                var secondClaim = new AzureProviderOperationStore(second).ClaimAsync(_workspaceId, operation.Id, "worker-b", "lease-b", TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);
                var claims = await Task.WhenAll(firstClaim, secondClaim);
                Assert.Equal(1, claims.Count(x => x is not null));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Stale_recovery_wins_against_heartbeat_checkpoint_and_finalize_versions()
    {
        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        var first = new AzureProviderOperationStore(firstDb);
        var second = new AzureProviderOperationStore(secondDb);
        foreach (var suffix in new[] { "heartbeat", "checkpoint", "finalize" })
        {
            var operation = await first.CreateOrGetAsync(Request() with { TargetKey = "workload-" + suffix, IdempotencyKey = suffix }, DateTimeOffset.UtcNow);
            var claimed = await first.ClaimAsync(_workspaceId, operation.Id, "worker-a", "lease-" + suffix, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow);
            Assert.NotNull(claimed);
            Assert.Equal(1, await second.RecoverStaleAsync(DateTimeOffset.UtcNow.AddMinutes(2)));
            if (suffix == "heartbeat")
                Assert.Null(await first.HeartbeatAsync(_workspaceId, operation.Id, "lease-heartbeat", TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow.AddMinutes(2), claimed!.Version));
            else if (suffix == "checkpoint")
                Assert.Null(await first.CheckpointAsync(_workspaceId, operation.Id, "lease-checkpoint", new(AzureProviderOperationPhase.FoundationReady, "checkpoint.ready", "Ready.", new(), null, AzureProviderHealth.Unknown, []), DateTimeOffset.UtcNow.AddMinutes(2), claimed!.Version));
            else
                Assert.Null(await first.FinalizeAsync(_workspaceId, operation.Id, "lease-finalize", AzureProviderOperationStatus.Succeeded, "operation.succeeded", DateTimeOffset.UtcNow.AddMinutes(2), claimed!.Version));
        }
    }

    private CatalogDbContext CreateContext() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_connection).Options);

    private AzureProviderOperationRequest Request() => new(
        _workspaceId, "workload-a", AzureProviderOperationAction.Reconcile, "request-1",
        new('a', 64), new('b', 64), "3.8.0", "3.8", "combined", "Dedicated", "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined", "sha256:" + new string('c', 64));

    public void Dispose() => _connection.Dispose();
}
