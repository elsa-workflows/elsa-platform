using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public static class HealingDatabaseServiceCollectionExtensions
{
    public const string DefaultSqliteConnectionString = "Data Source=elsa-healing.db";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_Healing";
    public const string SqliteMigrationsAssembly = "Elsa.Platform.Healing.Persistence.SqliteMigrations";
    public const string SqlServerMigrationsAssembly = "Elsa.Platform.Healing.Persistence.SqlServerMigrations";

    public static IServiceCollection AddHealingDbContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var databaseOptions = configuration
            .GetSection(HealingDatabaseOptions.SectionName)
            .Get<HealingDatabaseOptions>() ?? new HealingDatabaseOptions();
        var connectionString = configuration.GetConnectionString("Healing");
        services.AddDbContext<HealingDbContext>(options =>
        {
            switch (databaseOptions.Provider)
            {
                case HealingDatabaseProvider.Sqlite:
                    options.UseSqlite(
                        string.IsNullOrWhiteSpace(connectionString) ? DefaultSqliteConnectionString : connectionString,
                        sqlite =>
                        {
                            sqlite.MigrationsAssembly(SqliteMigrationsAssembly);
                            sqlite.MigrationsHistoryTable(MigrationsHistoryTable);
                        });
                    break;
                case HealingDatabaseProvider.SqlServer:
                    if (string.IsNullOrWhiteSpace(connectionString))
                        throw new InvalidOperationException("ConnectionStrings:Healing is required when Healing:Database:Provider is SqlServer.");

                    options.UseSqlServer(connectionString, sqlServer =>
                    {
                        sqlServer.MigrationsAssembly(SqlServerMigrationsAssembly);
                        sqlServer.MigrationsHistoryTable(MigrationsHistoryTable);
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Healing database provider '{databaseOptions.Provider}'.");
            }
        });
        return services;
    }
}
