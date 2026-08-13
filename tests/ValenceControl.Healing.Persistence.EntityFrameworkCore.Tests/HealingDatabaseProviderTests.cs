using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingDatabaseProviderTests
{
    private const string SqlServerConnectionString = "Server=localhost;Initial Catalog=ValenceControlHealing;User ID=test;Password=NotARealPassword!;Encrypt=False";

    [Fact]
    public void AddHealingDbContext_defaults_to_sqlite_with_healing_owned_migration_history()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly, db.GetService<IMigrationsAssembly>().Assembly.GetName().Name);
        Assert.Contains(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable, db.GetService<IHistoryRepository>().GetCreateScript());
    }

    [Fact]
    public void AddHealingDbContext_selects_sql_server_without_sharing_catalog_migration_history()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Healing:Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Healing"] = SqlServerConnectionString
        };
        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.Equal(HealingDatabaseServiceCollectionExtensions.SqlServerMigrationsAssembly, db.GetService<IMigrationsAssembly>().Assembly.GetName().Name);
        var createScript = db.GetService<IHistoryRepository>().GetCreateScript();
        Assert.Contains(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable, createScript);
        Assert.DoesNotContain("__EFMigrationsHistory_Catalog", createScript);
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddHealingDbContext(configuration);
        return services.BuildServiceProvider();
    }
}
