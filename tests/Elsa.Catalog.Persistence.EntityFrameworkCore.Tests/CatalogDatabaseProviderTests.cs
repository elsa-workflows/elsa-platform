using Elsa.Catalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Catalog.Persistence.EntityFrameworkCore.Tests;

public sealed class CatalogDatabaseProviderTests
{
    private const string TestSqlServerConnectionString = @"Server=(localdb)\MSSQLLocalDB;Initial Catalog=ElsaCatalogProviderTests;Integrated Security=True;Encrypt=False";

    [Fact]
    public void AddCatalogDbContext_defaults_to_sqlite()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        db.GetService<IMigrationsAssembly>().Assembly.GetName().Name
            .Should().Be(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
    }

    [Fact]
    public void AddCatalogDbContext_uses_sql_server_when_configured()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString()
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
        db.GetService<IMigrationsAssembly>().Assembly.GetName().Name
            .Should().Be(CatalogDatabaseServiceCollectionExtensions.SqlServerMigrationsAssembly);
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString());

        connectionString.ConnectTimeout.Should().Be(120);
        connectionString.ConnectRetryCount.Should().Be(3);
        connectionString.ConnectRetryInterval.Should().Be(10);
    }

    [Fact]
    public void AddCatalogDbContext_preserves_explicit_sql_server_resilience_settings()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:SqlServer:ConnectTimeoutSeconds"] = "60",
            ["Database:SqlServer:ConnectRetryCount"] = "3",
            ["Database:SqlServer:ConnectRetryIntervalSeconds"] = "10",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString("Connect Timeout=25", "Connect Retry Count=1", "Connect Retry Interval=2")
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString());

        connectionString.ConnectTimeout.Should().Be(25);
        connectionString.ConnectRetryCount.Should().Be(1);
        connectionString.ConnectRetryInterval.Should().Be(2);
    }

    [Fact]
    public void AddCatalogDbContext_applies_configured_sql_server_connection_resilience_defaults()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:SqlServer:ConnectTimeoutSeconds"] = "45",
            ["Database:SqlServer:ConnectRetryCount"] = "2",
            ["Database:SqlServer:ConnectRetryIntervalSeconds"] = "7",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString()
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString());

        connectionString.ConnectTimeout.Should().Be(45);
        connectionString.ConnectRetryCount.Should().Be(2);
        connectionString.ConnectRetryInterval.Should().Be(7);
    }

    [Fact]
    public void AddCatalogDbContext_preserves_explicit_sql_server_timeout_alias()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString("Timeout=25")
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString());

        connectionString.ConnectTimeout.Should().Be(25);
    }

    [Fact]
    public void AddCatalogDbContext_ignores_keywords_inside_quoted_sql_server_values()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString("Application Name=\"abc;Connect Retry Count=1\"")
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString());

        connectionString.ConnectRetryCount.Should().Be(3);
    }

    [Fact]
    public void AddCatalogDbContext_applies_configured_sql_server_execution_strategy_options()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:SqlServer:MaxRetryCount"] = "4",
            ["Database:SqlServer:MaxRetryDelay"] = "00:00:12",
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString()
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var strategy = db.Database.CreateExecutionStrategy().Should().BeOfType<SqlServerRetryingExecutionStrategy>().Subject;

        strategy.MaxRetryCount.Should().Be(4);
        strategy.MaxRetryDelay.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void AddCatalogDbContext_rejects_invalid_sql_server_connect_retry_interval(int connectRetryIntervalSeconds)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:SqlServer:ConnectRetryIntervalSeconds"] = connectRetryIntervalSeconds.ToString(),
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString()
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectRetryIntervalSeconds*between 1 and 60*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void AddCatalogDbContext_rejects_invalid_sql_server_connect_retry_count(int connectRetryCount)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:SqlServer:ConnectRetryCount"] = connectRetryCount.ToString(),
            ["ConnectionStrings:Catalog"] = SqlServerConnectionString()
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectRetryCount*between 0 and 255*");
    }

    [Fact]
    public void AddCatalogDbContext_requires_sql_server_connection_string()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer"
        };

        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Catalog*SqlServer*");
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddCatalogDbContext(configuration);

        return services.BuildServiceProvider();
    }

    private static string SqlServerConnectionString(params string[] options) =>
        options.Length == 0
            ? TestSqlServerConnectionString
            : $"{TestSqlServerConnectionString};{string.Join(';', options)}";
}
