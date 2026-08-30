using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class AzureProviderOperationMigrationTests
{
    [Fact]
    public async Task Sqlite_migration_creates_durable_operation_tables_and_indexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var db = new CatalogDbContext(options);

        await db.Database.MigrateAsync();

        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync();
        var indexes = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToListAsync();
        Assert.Contains("AzureProviderOperations", tables);
        Assert.Contains("AzureProviderOperationTransitions", tables);
        Assert.Contains("IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity", indexes);
        Assert.Contains("IX_AzureProviderOperations_WorkspaceId_TargetKey", indexes);
        Assert.Contains("IX_AzureProviderOperations_Status_LeaseExpiresAt_UpdatedAt_Id", indexes);
        Assert.DoesNotContain("IX_AzureProviderOperations_WorkspaceId_Status_LeaseExpiresAt_UpdatedAt", indexes);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
