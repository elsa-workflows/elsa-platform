using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

public static class CatalogDatabaseServiceCollectionExtensions
{
    public const string DefaultSqliteConnectionString = "Data Source=elsa-control-catalog.db";
    public const string SqliteMigrationsAssembly = "ElsaControl.PackageCatalog.Persistence.SqliteMigrations";
    public const string SqlServerMigrationsAssembly = "ElsaControl.PackageCatalog.Persistence.SqlServerMigrations";

    public static IServiceCollection AddCatalogDbContext(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureSqlServerOptions = null)
    {
        var databaseOptions = configuration
            .GetSection(CatalogDatabaseOptions.SectionName)
            .Get<CatalogDatabaseOptions>() ?? new CatalogDatabaseOptions();

        var connectionString = configuration.GetConnectionString("Catalog");

        services.AddDbContext<CatalogDbContext>(options =>
            ConfigureProvider(options, databaseOptions, connectionString, configureSqlServerOptions));

        return services;
    }

    private static void ConfigureProvider(
        DbContextOptionsBuilder options,
        CatalogDatabaseOptions databaseOptions,
        string? connectionString,
        Action<DbContextOptionsBuilder>? configureSqlServerOptions)
    {
        switch (databaseOptions.Provider)
        {
            case CatalogDatabaseProvider.Sqlite:
                var sqliteConnectionString = string.IsNullOrWhiteSpace(connectionString)
                    ? DefaultSqliteConnectionString
                    : connectionString;

                EnsureSqliteDirectoryExists(sqliteConnectionString);
                options.UseSqlite(sqliteConnectionString, sqlite =>
                {
                    sqlite.MigrationsAssembly(SqliteMigrationsAssembly);
                });
                break;

            case CatalogDatabaseProvider.SqlServer:
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("ConnectionStrings:Catalog is required when Database:Provider is SqlServer.");

                var sqlServerConnectionString = BuildSqlServerConnectionString(connectionString, databaseOptions.SqlServer);
                options.UseSqlServer(sqlServerConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(SqlServerMigrationsAssembly);
                    sqlServer.EnableRetryOnFailure(
                        databaseOptions.SqlServer.MaxRetryCount,
                        databaseOptions.SqlServer.MaxRetryDelay,
                        errorNumbersToAdd: null);
                });
                configureSqlServerOptions?.Invoke(options);
                break;

            default:
                throw new InvalidOperationException($"Unsupported catalog database provider '{databaseOptions.Provider}'.");
        }
    }

    private static string BuildSqlServerConnectionString(string connectionString, CatalogSqlServerOptions options)
    {
        if (options.ConnectRetryIntervalSeconds is < 1 or > 60)
            throw new InvalidOperationException("Database:SqlServer:ConnectRetryIntervalSeconds must be between 1 and 60 seconds.");

        if (options.ConnectRetryCount is < 0 or > 255)
            throw new InvalidOperationException("Database:SqlServer:ConnectRetryCount must be between 0 and 255.");

        var builder = new SqlConnectionStringBuilder(connectionString);

        if (!HasExplicitSqlServerKeyword(connectionString, "ConnectTimeout", "ConnectionTimeout", "Timeout"))
            builder.ConnectTimeout = options.ConnectTimeoutSeconds;

        if (!HasExplicitSqlServerKeyword(connectionString, "ConnectRetryCount"))
            builder.ConnectRetryCount = options.ConnectRetryCount;

        if (!HasExplicitSqlServerKeyword(connectionString, "ConnectRetryInterval"))
            builder.ConnectRetryInterval = options.ConnectRetryIntervalSeconds;

        return builder.ConnectionString;
    }

    private static bool HasExplicitSqlServerKeyword(string connectionString, params string[] aliases)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var normalizedAliases = aliases.Select(NormalizeSqlServerKeyword).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return builder.Keys
            .Cast<string>()
            .Select(NormalizeSqlServerKeyword)
            .Any(normalizedAliases.Contains);
    }

    private static string NormalizeSqlServerKeyword(string keyword) =>
        keyword.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static void EnsureSqliteDirectoryExists(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
