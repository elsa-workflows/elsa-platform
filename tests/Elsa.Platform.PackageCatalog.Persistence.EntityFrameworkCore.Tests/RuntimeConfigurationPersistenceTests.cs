using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Builder;
using Elsa.Platform.PackageCatalog.Core.RuntimeConfigurations;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeConfigurationPersistenceTests
{
    [Fact]
    public async Task Persists_runtime_configurations_and_versions()
    {
        await using var db = await CreateDbContextAsync();
        var workspace = new Workspace { Name = "Workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var store = new RuntimeConfigurationStore(db);
        var service = new RuntimeConfigurationService(store);

        var created = await service.CreateAsync(workspace.Id, "Runtime", null, MinimalIntent());
        var version = await service.CreateVersionAsync(workspace.Id, created.Id);

        db.ChangeTracker.Clear();
        (await store.ListAsync(workspace.Id)).Should().ContainSingle(x => x.Name == "Runtime");
        (await store.ListVersionsAsync(workspace.Id, created.Id)).Should().ContainSingle(x => x.VersionNumber == version!.VersionNumber);
    }

    [Fact]
    public async Task Migration_creates_runtime_configuration_tables()
    {
        await using var db = await CreateMigratedDbContextAsync();
        var workspace = new Workspace { Name = "Workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        db.RuntimeConfigurations.Add(new RuntimeConfiguration
        {
            WorkspaceId = workspace.Id,
            Name = "Runtime",
            IntentJson = RuntimeConfigurationService.SerializeIntent(MinimalIntent())
        });

        await db.SaveChangesAsync();

        (await db.RuntimeConfigurations.CountAsync()).Should().Be(1);
    }

    private static async Task<CatalogDbContext> CreateDbContextAsync()
    {
        var db = CreateDbContext(useMigrations: false);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<CatalogDbContext> CreateMigratedDbContextAsync()
    {
        var db = CreateDbContext(useMigrations: true);
        await db.Database.OpenConnectionAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    private static CatalogDbContext CreateDbContext(bool useMigrations)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite =>
            {
                if (useMigrations)
                    sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
            })
            .Options;
        return new CatalogDbContext(options);
    }

    private static RuntimeBuilderIntent MinimalIntent() =>
        new(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));
}
