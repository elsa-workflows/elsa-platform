using FluentAssertions;
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

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        db.GetService<IMigrationsAssembly>().Assembly.GetName().Name
            .Should().Be(HealingDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
        db.GetService<IHistoryRepository>().GetCreateScript()
            .Should().Contain(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable);
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

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
        db.GetService<IMigrationsAssembly>().Assembly.GetName().Name
            .Should().Be(HealingDatabaseServiceCollectionExtensions.SqlServerMigrationsAssembly);
        db.GetService<IHistoryRepository>().GetCreateScript()
            .Should().Contain(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable)
            .And.NotContain("__EFMigrationsHistory_Catalog");
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
