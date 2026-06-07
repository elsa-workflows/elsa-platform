using System.Diagnostics;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class DeploymentWorkspaceTierPersistenceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly DeploymentWorkspaceStore _store;
    private readonly Guid _workspaceId;
    private readonly Guid _accountId;

    public DeploymentWorkspaceTierPersistenceTests()
    {
        _db = CreateDbContext();
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        var workspace = new Workspace { Name = "Deployment Workspace" };
        var account = new Account { DisplayName = "Deployment User", Email = "deployment@example.test" };
        _db.Workspaces.Add(workspace);
        _db.Accounts.Add(account);
        _db.SaveChanges();

        _workspaceId = workspace.Id;
        _accountId = account.Id;
        _store = new DeploymentWorkspaceStore(_db);
    }

    [Fact]
    public async Task Seeds_default_tiers_and_legacy_capabilities()
    {
        var tiers = await _store.EnsureDefaultTiersAsync(_workspaceId, _accountId);

        tiers.Should().HaveCount(4);
        tiers.Should().Contain(x =>
            x.Name == EnvironmentTier.Dev.ToString()
            && x.IsDefault
            && x.Capabilities.SequenceEqual(DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Dev].Order(StringComparer.Ordinal)));
        tiers.Should().Contain(x =>
            x.Name == EnvironmentTier.Production.ToString()
            && x.Capabilities.Contains(DeploymentTierCapabilities.ProductionLike)
            && x.Capabilities.Contains(DeploymentTierCapabilities.ConfirmationRequired)
            && x.Capabilities.Contains(DeploymentTierCapabilities.RollbackEnabled));
        (await CountRowsAsync("DeploymentTierDefinitions")).Should().Be(4);
        (await CountRowsAsync("DeploymentTierChangeRecords")).Should().Be(4);
    }

    [Fact]
    public async Task Persists_tier_create_update_archive_restore_and_audit_records()
    {
        await _store.EnsureDefaultTiersAsync(_workspaceId);
        var created = await _store.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest(
                "UAT",
                "User acceptance",
                25,
                [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget],
                _accountId));

        var impact = await _store.PreviewTierImpactAsync(
            _workspaceId,
            created.Id,
            [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.SecretVerificationRequired]);
        var updated = await _store.UpdateTierAsync(
            _workspaceId,
            created.Id,
            new UpdateDeploymentTierRequest(
                "UAT",
                "Final validation",
                30,
                [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.SecretVerificationRequired],
                ImpactAccepted: true,
                _accountId),
            impact);
        var archived = await _store.ArchiveTierAsync(_workspaceId, created.Id, new ArchiveDeploymentTierRequest(_accountId));
        var restored = await _store.RestoreTierAsync(_workspaceId, created.Id, new RestoreDeploymentTierRequest(_accountId));

        updated.Description.Should().Be("Final validation");
        updated.Capabilities.Should().Contain(DeploymentTierCapabilities.SecretVerificationRequired);
        archived.Status.Should().Be(DeploymentTierStatus.Archived);
        restored.Status.Should().Be(DeploymentTierStatus.Active);
        (await CountRowsAsync("DeploymentTierChangeRecords", "TierId", created.Id)).Should().Be(4);
    }

    [Fact]
    public async Task Rejects_duplicate_active_tier_names()
    {
        await _store.EnsureDefaultTiersAsync(_workspaceId);
        await _store.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest("UAT", null, 50, [DeploymentTierCapabilities.PreproductionLike], null));

        var act = () => _store.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest("UAT", null, 60, [DeploymentTierCapabilities.TestLike], null));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Prevents_archiving_last_active_tier()
    {
        var tiers = await _store.EnsureDefaultTiersAsync(_workspaceId);
        foreach (var tier in tiers.Where(x => x.Name != EnvironmentTier.Dev.ToString()))
            await _store.ArchiveTierAsync(_workspaceId, tier.Id, new ArchiveDeploymentTierRequest(null));
        var lastActive = (await _store.ListTiersAsync(_workspaceId)).Single(x => x.Status == DeploymentTierStatus.Active);

        var act = () => _store.ArchiveTierAsync(_workspaceId, lastActive.Id, new ArchiveDeploymentTierRequest(null));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Assigns_environment_to_active_tier_and_preserves_archived_tier_reads()
    {
        var productionTier = (await _store.EnsureDefaultTiersAsync(_workspaceId)).Single(x => x.Name == EnvironmentTier.Production.ToString());
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await _store.CreateEnvironmentAsync(
            _workspaceId,
            new CreateDeploymentEnvironmentRequest(application.Id, "Production EU", EnvironmentTier.Production, productionTier.Id));

        await _store.ArchiveTierAsync(_workspaceId, productionTier.Id, new ArchiveDeploymentTierRequest(null));
        _db.ChangeTracker.Clear();
        var cockpit = await _store.GetCockpitAsync(_workspaceId);

        environment.TierId.Should().Be(productionTier.Id);
        cockpit.Applications.Single().Environments.Should().ContainSingle(x =>
            x.Id == environment.Id.ToString("D")
            && x.TierName == EnvironmentTier.Production.ToString()
            && x.TierStatus == DeploymentTierStatus.Archived.ToString()
            && x.TierCapabilities != null
            && x.TierCapabilities.Contains(DeploymentTierCapabilities.ProductionLike));
    }

    [Fact]
    public async Task Rejects_archived_and_cross_workspace_tier_assignment()
    {
        var productionTier = (await _store.EnsureDefaultTiersAsync(_workspaceId)).Single(x => x.Name == EnvironmentTier.Production.ToString());
        await _store.ArchiveTierAsync(_workspaceId, productionTier.Id, new ArchiveDeploymentTierRequest(null));
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var otherWorkspaceId = await CreateWorkspaceAsync("Other Workspace");
        var otherTier = (await _store.EnsureDefaultTiersAsync(otherWorkspaceId)).Single(x => x.Name == EnvironmentTier.Test.ToString());

        var archived = () => _store.CreateEnvironmentAsync(
            _workspaceId,
            new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production, productionTier.Id));
        var crossWorkspace = () => _store.CreateEnvironmentAsync(
            _workspaceId,
            new CreateDeploymentEnvironmentRequest(application.Id, "QA", EnvironmentTier.Test, otherTier.Id));

        await archived.Should().ThrowAsync<InvalidOperationException>();
        await crossWorkspace.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Backfills_missing_environment_tier_references_from_legacy_tier()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        await ClearEnvironmentTierReferenceAsync(environment.Id);

        var tiers = await _store.EnsureDefaultTiersAsync(_workspaceId);
        _db.ChangeTracker.Clear();
        var loaded = await _store.GetCockpitAsync(_workspaceId);
        var productionTier = tiers.Single(x => x.Name == EnvironmentTier.Production.ToString());

        loaded.Applications.Single().Environments.Should().ContainSingle(x =>
            x.Id == environment.Id.ToString("D")
            && x.TierName == productionTier.Name
            && x.TierCapabilities != null
            && x.TierCapabilities.Contains(DeploymentTierCapabilities.ProductionLike));
    }

    [Fact]
    public async Task Migration_creates_tier_tables()
    {
        await using var db = CreateMigratedDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Migrated Workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var store = new DeploymentWorkspaceStore(db);

        var tiers = await store.EnsureDefaultTiersAsync(workspace.Id);

        tiers.Should().HaveCount(4);
    }

    [Fact]
    public async Task Migration_backfills_default_tiers_and_environment_references()
    {
        await using var db = CreateMigratedDbContext();
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260526130000_AddWorkspaceDeploymentArtifacts");
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Workspaces (Id, Name, Kind, CreatedAt, UpdatedAt, SoftDeletedAt)
            VALUES ({workspaceId}, 'Migrated Workspace', 0, {now}, {now}, NULL)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DeploymentApplications (Id, WorkspaceId, Name, Description, CreatedAt, UpdatedAt, CreatedByAccountId, UpdatedByAccountId)
            VALUES ({applicationId}, {workspaceId}, 'Claims', NULL, 0, 0, NULL, NULL)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DeploymentEnvironments (
                Id,
                WorkspaceId,
                ApplicationId,
                Name,
                Tier,
                DesiredRevisionId,
                DeployedRevisionId,
                DeploymentStatus,
                DriftStatus,
                CreatedAt,
                UpdatedAt
            )
            VALUES ({environmentId}, {workspaceId}, {applicationId}, 'Prod', 'Production', NULL, NULL, 'Idle', 'InSync', 0, 0)
            """);

        await migrator.MigrateAsync("20260527131902_AddCustomDeploymentTiers");

        (await CountRowsAsync(db, "DeploymentTierDefinitions")).Should().Be(4);
        (await CountRowsAsync(db, "DeploymentTierCapabilityAssignments")).Should().Be(15);
        var productionCapabilityCount = await CountRowsAsync(
            db,
            """
            SELECT COUNT(*)
            FROM DeploymentEnvironments environments
            JOIN DeploymentTierDefinitions tiers ON tiers.Id = environments.TierId
            JOIN DeploymentTierCapabilityAssignments capabilities ON capabilities.TierId = tiers.Id
            WHERE environments.Id = $value
              AND tiers.Name = 'Production'
              AND capabilities.CapabilityId = 'deployment.tier.production-like'
            """,
            environmentId);
        productionCapabilityCount.Should().Be(1);

        var store = new DeploymentWorkspaceStore(db);
        var productionTier = (await store.ListTiersAsync(workspaceId)).Single(x => x.Name == EnvironmentTier.Production.ToString());
        var createdEnvironment = await store.CreateEnvironmentAsync(
            workspaceId,
            new CreateDeploymentEnvironmentRequest(applicationId, "Prod EU", EnvironmentTier.Production, productionTier.Id));

        createdEnvironment.TierId.Should().Be(productionTier.Id);
    }

    [Fact]
    public async Task Lists_normal_workspace_dataset_under_three_seconds()
    {
        var defaults = await _store.EnsureDefaultTiersAsync(_workspaceId);
        for (var i = 0; i < 16; i++)
        {
            await _store.CreateTierAsync(
                _workspaceId,
                new CreateDeploymentTierRequest($"Custom {i:00}", null, 100 + i, [DeploymentTierCapabilities.TestLike], null));
        }
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        for (var i = 0; i < 250; i++)
        {
            var tier = defaults[i % defaults.Count];
            await _store.CreateEnvironmentAsync(
                _workspaceId,
                new CreateDeploymentEnvironmentRequest(application.Id, $"Environment {i:000}", EnvironmentTier.Test, tier.Id));
        }

        var stopwatch = Stopwatch.StartNew();
        var tiers = await _store.ListTiersAsync(_workspaceId);
        stopwatch.Stop();

        tiers.Should().HaveCount(20);
        tiers.Sum(x => x.EnvironmentCount).Should().Be(250);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Guid> CreateWorkspaceAsync(string name)
    {
        var workspace = new Workspace { Name = name };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();
        return workspace.Id;
    }

    private async Task ClearEnvironmentTierReferenceAsync(Guid environmentId)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE DeploymentEnvironments
            SET TierId = NULL
            WHERE Id = {environmentId}
            """);
    }

    private Task<long> CountRowsAsync(string table) => CountRowsAsync(_db, table);

    private static async Task<long> CountRowsAsync(CatalogDbContext db, string table)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt64(count);
    }

    private Task<long> CountRowsAsync(string table, string column, Guid value) =>
        CountRowsAsync(_db, $"SELECT COUNT(*) FROM {table} WHERE {column} = $value", value);

    private static async Task<long> CountRowsAsync(CatalogDbContext db, string sql, Guid value)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$value";
        parameter.Value = value;
        command.Parameters.Add(parameter);
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt64(count);
    }

    private static CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }

    private static CatalogDbContext CreateMigratedDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        return new CatalogDbContext(options);
    }
}
