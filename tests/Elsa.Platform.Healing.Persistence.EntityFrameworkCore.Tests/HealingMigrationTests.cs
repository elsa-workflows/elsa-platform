using Elsa.Platform.Healing.Core;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingMigrationTests
{
    private const string MigrationId = "20260716000000_InitialHealing";

    [Fact]
    public async Task Sqlite_migration_applies_and_database_blocks_bulk_audit_mutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var db = new HealingDbContext(options);
        await db.Database.MigrateAsync();
        var auditEvent = CreateAuditEvent();
        await new HealingStore(db).AppendAsync(auditEvent);

        var update = () => db.Set<HealingAuditEvent>().Where(x => x.Id == auditEvent.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReasonCode, "tampered"));
        var delete = () => db.Set<HealingAuditEvent>().Where(x => x.Id == auditEvent.Id).ExecuteDeleteAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(MigrationId);
        await update.Should().ThrowAsync<Exception>().WithMessage("*append-only*");
        await delete.Should().ThrowAsync<Exception>().WithMessage("*append-only*");
    }

    [Fact]
    public void Both_provider_migrations_include_the_complete_model_and_audit_guard()
    {
        using var sqlite = CreateSqliteContext();
        using var sqlServer = CreateSqlServerContext();

        AssertMigration(sqlite, "TR_HealingAuditEvents_BlockUpdate");
        AssertMigration(sqlServer, "TR_HealingAuditEvents_BlockMutation");
        sqlite.Model.GetEntityTypes().Select(x => x.GetTableName()).Where(x => x is not null).Distinct()
            .Should().BeEquivalentTo(sqlServer.Model.GetEntityTypes().Select(x => x.GetTableName()).Where(x => x is not null).Distinct());
    }

    private static void AssertMigration(HealingDbContext db, string triggerName)
    {
        var migrations = db.GetService<IMigrationsAssembly>();
        migrations.Migrations.Should().ContainKey(MigrationId);
        var migration = migrations.CreateMigration(migrations.Migrations[MigrationId], db.Database.ProviderName!);
        migration.UpOperations.OfType<CreateTableOperation>().Should().NotBeEmpty();
        migration.UpOperations.OfType<SqlOperation>().Should().ContainSingle(x => x.Sql.Contains(triggerName, StringComparison.Ordinal));
    }

    private static HealingDbContext CreateSqliteContext() => new(
        new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options);

    private static HealingDbContext CreateSqlServerContext() => new(
        new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=ElsaHealing;User ID=test;Password=not-real;Encrypt=False",
                sqlServer => sqlServer.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqlServerMigrationsAssembly))
            .Options);

    private static HealingAuditEvent CreateAuditEvent() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        AggregateType = "incident",
        AggregateId = Guid.NewGuid(),
        EventType = "incident.observed",
        ReasonCode = "accepted",
        ActorType = "platform",
        ActorId = "healing-inbox",
        CorrelationId = Guid.NewGuid(),
        SafeDetailJson = "{}",
        OccurredAt = DateTimeOffset.UtcNow
    };
}
