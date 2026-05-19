namespace Elsa.Catalog.Persistence.EntityFrameworkCore;

public sealed class CatalogDatabaseOptions
{
    public const string SectionName = "Database";

    public CatalogDatabaseProvider Provider { get; set; } = CatalogDatabaseProvider.Sqlite;
    public CatalogSqlServerOptions SqlServer { get; set; } = new();
}

public sealed class CatalogSqlServerOptions
{
    public int ConnectTimeoutSeconds { get; set; } = 120;
    public int ConnectRetryCount { get; set; } = 3;
    public int ConnectRetryIntervalSeconds { get; set; } = 10;
    public int MaxRetryCount { get; set; } = 6;
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
