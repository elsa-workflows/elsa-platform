using ElsaControl.Deployment.Core.Instances;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed partial class ElsaInstanceLifecycleStoreTests
{
    [Fact]
    public async Task Recovery_ledger_rollback_preserves_a_real_accepted_lifecycle_request()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = await CreateWorkspaceAsync(db, "Recovery rollback workspace");
        var service = new ElsaInstanceLifecycleService(CreateStore(db), new FixedTimeProvider(Now));
        var created = await service.CreateAsync(new ElsaInstanceCreateRequest(
            workspace.OrganizationId, workspace.Id, "Managed Elsa", "recovery-rollback-elsa",
            CreateIntent(), "create-recovery-rollback"));
        await MarkRecoveryRequiredAsync(db, created.Operation.Id, "a");
        var current = await CreateStore(db).GetInstanceAsync(workspace.Id, created.Instance.Id);
        var request = new ElsaInstanceLifecycleRequest(
            workspace.Id, created.Instance.Id, current!.Version, "recover-before-rollback");
        var accepted = await service.RecoverAsync(request);
        var recoveryId = (await db.ElsaInstanceRecoveryRequests.AsNoTracking().SingleAsync()).Id;

        await db.Database.MigrateAsync("20260906005158_AddAzureProviderRecoveryObservations");

        Assert.Equal(1, await db.Database.SqlQueryRaw<long>(
            "SELECT COUNT(*) AS Value FROM ElsaInstanceRecoveryRequests").SingleAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM ElsaInstanceRecoveryRequests WHERE Id = {recoveryId}
            """));
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var replay = await service.RecoverAsync(request);
        Assert.True(replay.Replayed);
        Assert.Equal(accepted.Operation.Id, replay.Operation.Id);
        Assert.Equal(accepted.Operation.AttemptNumber, replay.Operation.AttemptNumber);
        Assert.Equal(recoveryId, (await db.ElsaInstanceRecoveryRequests.AsNoTracking().SingleAsync()).Id);
    }
}
