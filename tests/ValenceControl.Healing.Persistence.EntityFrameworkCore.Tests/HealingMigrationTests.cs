using ValenceControl.Healing.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingMigrationTests
{
    private static readonly string[] ExpectedMigrationNames =
    [
        "InitialHealing",
        "AddRepairOrchestration",
        "AddMergeVerificationAndReporting",
        "AddQueryableRepairUsage",
        "AddRepairVerificationFailureOutbox",
        "AddManagedInferenceReservations"
    ];

    [Fact]
    public async Task Sqlite_migrations_apply_from_empty_and_reopen_at_latest_while_preserving_the_audit_guard()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var db = new HealingDbContext(options);
        var migrator = db.GetService<IMigrator>();
        var latestMigration = db.GetService<IMigrationsAssembly>().Migrations.Keys.Last();

        await migrator.MigrateAsync(latestMigration);

        Assert.Equal(ExpectedMigrationNames, MigrationNames(await db.Database.GetAppliedMigrationsAsync()));
        var tableNames = await TableNamesAsync(connection);
        Assert.Contains("HealingIncidents", tableNames);
        Assert.Contains("HealingRepairWorkItemProjections", tableNames);
        Assert.Contains("HealingProviderActorIdentityLinks", tableNames);
        Assert.Contains("HealingRepairVerificationFailureOutbox", tableNames);
        Assert.Contains("HealingManagedRepairInferenceReservations", tableNames);
        Assert.Contains("IdempotencyKey", await ColumnNamesAsync(connection, "HealingHumanCommands"));
        Assert.Contains("ProviderActorLogin", await ColumnNamesAsync(connection, "HealingHumanCommands"));

        var auditEvent = CreateAuditEvent();
        await new HealingStore(db).AppendAsync(auditEvent);
        var update = () => db.Set<HealingAuditEvent>().Where(x => x.Id == auditEvent.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReasonCode, "tampered"));
        var delete = () => db.Set<HealingAuditEvent>().Where(x => x.Id == auditEvent.Id).ExecuteDeleteAsync();
        Assert.Contains("append-only", (await Assert.ThrowsAnyAsync<Exception>(update)).Message);
        Assert.Contains("append-only", (await Assert.ThrowsAnyAsync<Exception>(delete)).Message);

        await using var reopened = new HealingDbContext(options);
        await reopened.GetService<IMigrator>().MigrateAsync(latestMigration);
        Assert.Equal(ExpectedMigrationNames, MigrationNames(await reopened.Database.GetAppliedMigrationsAsync()));
        Assert.Equal(0, (await reopened.HealingIncidents.CountAsync()));
    }

    [Fact]
    public async Task Latest_sqlite_migration_round_trips_and_backfills_unique_keys_for_existing_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var db = new HealingDbContext(options);
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        var targetIndex = Array.FindIndex(migrations, x => x.EndsWith("AddMergeVerificationAndReporting", StringComparison.Ordinal));
        var previousMigration = migrations[targetIndex - 1];
        var latestMigration = migrations[targetIndex];
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(previousMigration);
        await InsertPreMigrationRowsAsync(connection);

        await migrator.MigrateAsync(latestMigration);
        Assert.Equal(2, (await CountAsync(connection, "HealingHumanCommands", "IdempotencyKey")));
        Assert.Equal(2, (await CountAsync(connection, "HealingDeploymentObservations", "SourceObservationId")));

        await migrator.MigrateAsync(previousMigration);
        Assert.DoesNotContain("IdempotencyKey", (await ColumnNamesAsync(connection, "HealingHumanCommands")));
        Assert.DoesNotContain("SourceObservationId", (await ColumnNamesAsync(connection, "HealingDeploymentObservations")));

        await migrator.MigrateAsync(latestMigration);
        Assert.Equal(2, (await CountAsync(connection, "HealingHumanCommands", "IdempotencyKey")));
        Assert.Equal(2, (await CountAsync(connection, "HealingDeploymentObservations", "SourceObservationId")));
    }

    [Fact]
    public async Task Queryable_usage_migration_is_reversible()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var db = new HealingDbContext(options);
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        var latestIndex = Array.FindIndex(migrations, x => x.EndsWith("AddQueryableRepairUsage", StringComparison.Ordinal));
        var previousMigration = migrations[latestIndex - 1];
        var latestMigration = migrations[latestIndex];
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(previousMigration);
        Assert.DoesNotContain("InputUnits", (await ColumnNamesAsync(connection, "HealingRepairAttempts")));

        await migrator.MigrateAsync(latestMigration);
        var repairAttemptColumns = await ColumnNamesAsync(connection, "HealingRepairAttempts");
        Assert.Contains("InputUnits", repairAttemptColumns);
        Assert.Contains("OutputUnits", repairAttemptColumns);
        Assert.Contains("AgentDurationTicks", repairAttemptColumns);
        Assert.Contains("RepositoryRunDurationTicks", repairAttemptColumns);
        Assert.Contains("RepositoryRuns", repairAttemptColumns);

        await migrator.MigrateAsync(previousMigration);
        Assert.DoesNotContain("InputUnits", (await ColumnNamesAsync(connection, "HealingRepairAttempts")));
    }

    [Fact]
    public void Sqlite_and_sql_server_migration_chains_are_logically_equivalent_and_reversible()
    {
        using var sqlite = CreateSqliteContext();
        using var sqlServer = CreateSqlServerContext();
        var sqliteMigrations = sqlite.GetService<IMigrationsAssembly>();
        var sqlServerMigrations = sqlServer.GetService<IMigrationsAssembly>();

        Assert.Equal(ExpectedMigrationNames, MigrationNames(sqliteMigrations.Migrations.Keys));
        Assert.Equal(ExpectedMigrationNames, MigrationNames(sqlServerMigrations.Migrations.Keys));

        var sqliteByName = ByLogicalName(sqliteMigrations);
        var sqlServerByName = ByLogicalName(sqlServerMigrations);
        foreach (var migrationName in ExpectedMigrationNames)
        {
            var sqliteMigration = sqliteMigrations.CreateMigration(
                sqliteByName[migrationName].Value,
                sqlite.Database.ProviderName!);
            var sqlServerMigration = sqlServerMigrations.CreateMigration(
                sqlServerByName[migrationName].Value,
                sqlServer.Database.ProviderName!);

            AssertEquivalentOperations(migrationName, "up", sqliteMigration.UpOperations, sqlServerMigration.UpOperations);
            AssertEquivalentOperations(migrationName, "down", sqliteMigration.DownOperations, sqlServerMigration.DownOperations);
            Assert.NotEmpty(sqliteMigration.DownOperations);
            Assert.NotEmpty(sqlServerMigration.DownOperations);
        }

        AssertAuditGuard(sqlite, sqliteByName["InitialHealing"].Key, "TR_HealingAuditEvents_BlockUpdate");
        AssertAuditGuard(sqlServer, sqlServerByName["InitialHealing"].Key, "TR_HealingAuditEvents_BlockMutation");

        AssertLatestMigrationScript(sqlite);
        AssertLatestMigrationScript(sqlServer);
    }

    private static void AssertLatestMigrationScript(HealingDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        var latestMigration = db.GetService<IMigrationsAssembly>().Migrations.Keys.Last();
        var upScript = migrator.GenerateScript(Migration.InitialDatabase, latestMigration);

        Assert.Contains("HealingProviderActorIdentityLinks", upScript);
        Assert.Contains("HealingRepairVerificationFailureOutbox", upScript);
        Assert.Contains("HealingManagedRepairInferenceReservations", upScript);
        Assert.Contains("IdempotencyKey", upScript);
    }

    private static void AssertAuditGuard(HealingDbContext db, string migrationId, string triggerName)
    {
        var migrations = db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(migrations.Migrations[migrationId], db.Database.ProviderName!);
        Assert.Contains(migration.UpOperations.OfType<SqlOperation>(), x => x.Sql.Contains(triggerName, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, KeyValuePair<string, TypeInfo>> ByLogicalName(IMigrationsAssembly migrations) =>
        migrations.Migrations.ToDictionary(x => MigrationName(x.Key), StringComparer.Ordinal);

    private static string[] MigrationNames(IEnumerable<string> migrationIds) =>
        migrationIds.Select(MigrationName).ToArray();

    private static string MigrationName(string migrationId)
    {
        var separator = migrationId.IndexOf('_', StringComparison.Ordinal);
        return separator < 0 ? migrationId : migrationId[(separator + 1)..];
    }

    private static string[] OperationSignatures(IReadOnlyList<MigrationOperation> operations) =>
        operations
            .Where(x => x is not SqlOperation)
            .Select(OperationSignature)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertEquivalentOperations(
        string migrationName,
        string direction,
        IReadOnlyList<MigrationOperation> sqliteOperations,
        IReadOnlyList<MigrationOperation> sqlServerOperations)
    {
        var sqlite = OperationSignatures(sqliteOperations);
        var sqlServer = OperationSignatures(sqlServerOperations);
        var onlySqlite = sqlite.Except(sqlServer, StringComparer.Ordinal).ToArray();
        var onlySqlServer = sqlServer.Except(sqlite, StringComparer.Ordinal).ToArray();

        Assert.Empty(onlySqlite);
        Assert.Empty(onlySqlServer);
        Assert.Equal(sqlServer.Length, sqlite.Length);
    }

    private static string OperationSignature(MigrationOperation operation) => operation switch
    {
        CreateTableOperation x => $"create-table:{x.Name}:columns={Columns(x.Columns)}:pk={Names(x.PrimaryKey?.Columns)}:fk={ForeignKeys(x.ForeignKeys)}",
        DropTableOperation x => $"drop-table:{x.Name}",
        AddColumnOperation x => $"add-column:{x.Table}:{Column(x)}",
        DropColumnOperation x => $"drop-column:{x.Table}:{x.Name}",
        CreateIndexOperation x => $"create-index:{x.Table}:{x.Name}:{Names(x.Columns)}:unique={x.IsUnique}",
        DropIndexOperation x => $"drop-index:{x.Table}:{x.Name}",
        AddForeignKeyOperation x => $"add-fk:{ForeignKey(x)}",
        DropForeignKeyOperation x => $"drop-fk:{x.Table}:{x.Name}",
        _ => $"{operation.GetType().Name}"
    };

    private static string Columns(IEnumerable<AddColumnOperation> columns) =>
        string.Join(';', columns.Select(Column).Order(StringComparer.Ordinal));

    private static string Column(ColumnOperation column) =>
        $"{column.Name}:nullable={column.IsNullable}:max={column.MaxLength}";

    private static string ForeignKeys(IEnumerable<AddForeignKeyOperation> foreignKeys) =>
        string.Join(';', foreignKeys.Select(ForeignKey).Order(StringComparer.Ordinal));

    private static string ForeignKey(AddForeignKeyOperation foreignKey) =>
        $"{foreignKey.Table}:{foreignKey.Name}:{Names(foreignKey.Columns)}->{foreignKey.PrincipalTable}:{Names(foreignKey.PrincipalColumns)}:{foreignKey.OnDelete}";

    private static string Names(IEnumerable<string>? names) =>
        names is null ? string.Empty : string.Join(',', names);

    private static async Task<string[]> TableNamesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        return await ReadStringsAsync(command);
    }

    private static async Task<string[]> ColumnNamesAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(1));
        return names.ToArray();
    }

    private static async Task<string[]> ReadStringsAsync(SqliteCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static async Task InsertPreMigrationRowsAsync(SqliteConnection connection)
    {
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        await using (var incidentCommand = connection.CreateCommand())
        {
            incidentCommand.CommandText = """
                INSERT INTO "HealingIncidents"
                    ("Id", "WorkspaceId", "ApplicationId", "FingerprintVersion", "Fingerprint", "RepairRepositoryKey",
                     "Status", "Severity", "Classification", "SelectedBindingId", "SelectedComponentEntryId",
                     "FirstSeenAt", "LastSeenAt", "OccurrenceCount", "ActiveEpisodeId", "WorkItemProjectionId",
                     "NeedsHumanReason", "ReadyAfter", "Version")
                VALUES
                    ($incidentId, $workspaceId, $applicationId, 'v1', 'fingerprint', 'repository',
                     0, 0, 0, NULL, NULL, 1, 1, 1, NULL, NULL, NULL, NULL, X'00');
                """;
            incidentCommand.Parameters.AddWithValue("$incidentId", incidentId.ToString("D"));
            incidentCommand.Parameters.AddWithValue("$workspaceId", workspaceId.ToString("D"));
            incidentCommand.Parameters.AddWithValue("$applicationId", applicationId.ToString("D"));
            await incidentCommand.ExecuteNonQueryAsync();
        }
        for (var index = 0; index < 2; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "HealingHumanCommands"
                    ("Id", "WorkspaceId", "ApplicationId", "IncidentId", "Command", "ProviderActorId",
                     "ControlActorId", "ProviderPermissionSnapshotJson", "WorkspacePermissionGranted",
                     "ConfirmationId", "Status", "ResultCode", "SafeResultDetail", "RequestedAt", "CompletedAt", "Version")
                VALUES
                    ($id, $workspaceId, $applicationId, $incidentId, 'stop', 'provider-actor', NULL, '{}', 1,
                     NULL, 0, NULL, NULL, 1, NULL, X'00');
                INSERT INTO "HealingDeploymentObservations"
                    ("Id", "WorkspaceId", "ApplicationId", "EnvironmentId", "Revision", "DeployedAt", "Source",
                     "SourceIdempotencyKey", "TrustIdentity", "EvidenceDigest", "AcceptedAt")
                VALUES
                    ($deploymentId, $workspaceId, $applicationId, $environmentId, 'revision', 1, 0,
                     $sourceKey, 'trusted-test', 'digest', 1);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$deploymentId", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$workspaceId", workspaceId.ToString("D"));
            command.Parameters.AddWithValue("$applicationId", applicationId.ToString("D"));
            command.Parameters.AddWithValue("$incidentId", incidentId.ToString("D"));
            command.Parameters.AddWithValue("$environmentId", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$sourceKey", $"source-{index}");
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(DISTINCT \"{columnName}\") FROM \"{tableName}\" WHERE \"{columnName}\" <> '';";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static HealingDbContext CreateSqliteContext() => new(
        new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite =>
                sqlite.MigrationsAssembly(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options);

    private static HealingDbContext CreateSqlServerContext() => new(
        new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=ValenceControlHealing;User ID=test;Password=not-real;Encrypt=False",
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
        ActorType = "control",
        ActorId = "healing-inbox",
        CorrelationId = Guid.NewGuid(),
        SafeDetailJson = "{}",
        OccurredAt = DateTimeOffset.UtcNow
    };
}
