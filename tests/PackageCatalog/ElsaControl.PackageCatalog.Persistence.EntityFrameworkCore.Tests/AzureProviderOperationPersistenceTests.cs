using ElsaControl.Deployment.Azure;
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
        Assert.Single(await store.ListTransitionsAsync(_workspaceId, first.Id));
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
            AzureProviderOperationPhase.FoundationReady, "foundation.ready", "Foundation is ready.",
            new(ResourceGroupName: "rg-safe", FoundationDeploymentId: "deployment-1"), null, AzureProviderHealth.Unknown, []), now);
        Assert.Equal(AzureProviderOperationPhase.FoundationReady, checkpoint?.Phase);
        var replay = await store.CheckpointAsync(_workspaceId, operation.Id, "lease-1", new(
            AzureProviderOperationPhase.FoundationReady, "foundation.ready", "Foundation is ready.",
            new(ResourceGroupName: "rg-safe", FoundationDeploymentId: "deployment-1"), null, AzureProviderHealth.Unknown, []), now);
        Assert.Equal(checkpoint?.Version, replay?.Version);
        var completed = await store.FinalizeAsync(_workspaceId, operation.Id, "lease-1", AzureProviderOperationStatus.Succeeded, "operation.succeeded", "Completed.", now);
        Assert.Equal(AzureProviderOperationStatus.Succeeded, completed?.Status);
        Assert.Equal(4, (await store.ListTransitionsAsync(_workspaceId, operation.Id)).Count);
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

    private CatalogDbContext CreateContext() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_connection).Options);

    private AzureProviderOperationRequest Request() => new(
        _workspaceId, "workload-a", AzureProviderOperationAction.Reconcile, "request-1",
        new('a', 64), new('b', 64), "3.8.0", "3.8", "combined", "Dedicated", "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined", "sha256:" + new string('c', 64));

    public void Dispose() => _connection.Dispose();
}
