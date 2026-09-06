using Azure.Core;
using ElsaControl.Api.Catalog;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class CatalogSqlManagedIdentityEfTests
{
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unchanged_owned_connection_passes_open_guard(bool useMaster, bool async)
    {
        using var services = CreateServices(out var interceptor);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
#pragma warning disable EF1001
        using var master = useMaster ? db.GetService<ISqlServerConnection>().CreateMasterConnection() : null;
#pragma warning restore EF1001
        var connection = master?.DbConnection ?? db.Database.GetDbConnection();

        var result = async
            ? await interceptor.ConnectionOpeningAsync(connection, null!, default)
            : interceptor.ConnectionOpening(connection, null!, default);

        Assert.False(result.IsSuppressed);
    }

    [Fact]
    public void Sql_builder_keyword_support_is_not_explicit_authentication_presence()
    {
        var settings = new SqlConnectionStringBuilder("Server=example.test;Database=Catalog");
        Assert.True(settings.ContainsKey("Authentication"));
        Assert.True(settings.TryGetValue("Authentication", out _));
        Assert.False(settings.ShouldSerialize("Authentication"));
        settings.Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
        Assert.True(settings.ShouldSerialize("Authentication"));
        settings.Remove("Authentication");
        Assert.True(settings.ContainsKey("Authentication"));
        Assert.False(settings.ShouldSerialize("Authentication"));
    }

    [Fact]
    public void Scoped_contexts_own_distinct_connections_with_one_stable_callback()
    {
        using var services = CreateServices(out _);
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var first = Assert.IsType<SqlConnection>(firstDb.Database.GetDbConnection());
        var second = Assert.IsType<SqlConnection>(secondDb.Database.GetDbConnection());

        Assert.NotSame(first, second);
        Assert.NotNull(first.AccessTokenCallback);
        Assert.Same(first.AccessTokenCallback, second.AccessTokenCallback);
        Assert.Null(first.AccessToken);
        Assert.Null(second.AccessToken);
        Assert.IsType<SqlServerRetryingExecutionStrategy>(firstDb.Database.CreateExecutionStrategy());
        var settings = new SqlConnectionStringBuilder(first.ConnectionString);
        Assert.Equal(120, settings.ConnectTimeout);
        Assert.Equal(3, settings.ConnectRetryCount);
        Assert.Equal(10, settings.ConnectRetryInterval);
    }

    [Fact]
    public void Migration_master_connection_keeps_identity_callback_after_catalog_connection_creation()
    {
        using var services = CreateServices(out _);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var catalog = Assert.IsType<SqlConnection>(db.Database.GetDbConnection());
        // Exercise the exact pinned EF migration seam; no internal EF API is used in production.
#pragma warning disable EF1001
        using var master = db.GetService<ISqlServerConnection>().CreateMasterConnection();
#pragma warning restore EF1001
        var connection = Assert.IsType<SqlConnection>(master.DbConnection);

        Assert.NotSame(catalog, connection);
        Assert.Same(catalog.AccessTokenCallback, connection.AccessTokenCallback);
        Assert.NotNull(connection.AccessTokenCallback);
        var settings = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("master", settings.InitialCatalog);
        Assert.Equal("catalog.database.windows.net", settings.DataSource);
        Assert.Equal(ClientId, settings.UserID);
        Assert.Equal(SqlAuthenticationMethod.NotSpecified, settings.Authentication);
        Assert.True(settings.Encrypt);
        Assert.False(settings.TrustServerCertificate);
    }

    [Theory]
    [InlineData("Data Source", "other.database.windows.net")]
    [InlineData("Initial Catalog", "Other")]
    [InlineData("Encrypt", "False")]
    [InlineData("Trust Server Certificate", "True")]
    public async Task Owned_connection_target_and_tls_mutation_fail_before_open(string keyword, string value)
    {
        using var services = CreateServices(out var interceptor);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connection = Assert.IsType<SqlConnection>(db.Database.GetDbConnection());
        var settings = new SqlConnectionStringBuilder(connection.ConnectionString) { [keyword] = value };
        connection.ConnectionString = settings.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => interceptor.ConnectionOpening(connection, null!, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.ConnectionOpeningAsync(connection, null!, default).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clearing_or_replacing_owned_callback_cannot_bypass_open_guard(bool replace)
    {
        using var services = CreateServices(out var interceptor);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var connection = Assert.IsType<SqlConnection>(db.Database.GetDbConnection());
        connection.AccessTokenCallback = replace
            ? (_, _) => throw new InvalidOperationException("Replacement must never run.")
            : null;

        Assert.Throws<InvalidOperationException>(() => interceptor.ConnectionOpening(connection, null!, default));
    }

    private static ServiceProvider CreateServices(out CatalogSqlManagedIdentityConnectionInterceptor interceptor)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Catalog"] = $"Server=catalog.database.windows.net;Database=Catalog;Authentication=Active Directory Managed Identity;User Id={ClientId};Encrypt=True;TrustServerCertificate=False"
        }).Build();
        var instance = new CatalogSqlManagedIdentityConnectionInterceptor(new NoNetworkCredential(), ClientId);
        interceptor = instance;
        return new ServiceCollection().AddCatalogDbContext(configuration, options => options.AddInterceptors(instance))
            .BuildServiceProvider();
    }

    private sealed class NoNetworkCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connection construction must not acquire tokens.");
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connection construction must not acquire tokens.");
    }
}
