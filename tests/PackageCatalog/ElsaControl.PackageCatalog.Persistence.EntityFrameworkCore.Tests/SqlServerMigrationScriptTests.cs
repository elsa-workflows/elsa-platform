using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class SqlServerMigrationScriptTests
{
    [Fact]
    public void Full_idempotent_script_uses_dynamic_binding_for_historical_backfills()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer(
                @"Server=(localdb)\MSSQLLocalDB;Initial Catalog=ElsaControlMigrationScriptTests;Integrated Security=True;Encrypt=False",
                sqlServer => sqlServer.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqlServerMigrationsAssembly))
            .Options;
        using var db = new CatalogDbContext(options);

        var script = db.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("EXEC(N'UPDATE [SyncRuns]", script, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'UPDATE Packages", script, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'INSERT INTO DeploymentTierDefinitions", script, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'UPDATE StructuredDesiredStateRecords", script, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'INSERT INTO Organizations", script, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'INSERT INTO WorkspacePermissionAuditRecords", script, StringComparison.Ordinal);
        var normalizedScript = script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        Assert.DoesNotContain("BEGIN\n    UPDATE [SyncRuns]\n    SET [CompletedAtTicks]", normalizedScript, StringComparison.Ordinal);
    }
}
