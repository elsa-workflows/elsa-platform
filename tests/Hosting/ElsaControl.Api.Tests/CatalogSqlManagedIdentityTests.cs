using Azure.Core;
using ElsaControl.Api.Catalog;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace ElsaControl.Api.Tests;

public sealed class CatalogSqlManagedIdentityTests
{
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string OtherClientId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Factory_uses_only_explicit_managed_identity_authentication()
    {
        var managedIdentity = CreateInterceptor($"Authentication=Active Directory Managed Identity;User Id={ClientId}");
        var msiAlias = CreateInterceptor($"Authentication=Active Directory MSI;User Id={ClientId}");
        var defaultIdentity = CreateInterceptor("Authentication=Active Directory Default");

        Assert.NotNull(managedIdentity);
        Assert.NotNull(msiAlias);
        Assert.Null(defaultIdentity);
    }

    [Fact]
    public void Factory_preserves_system_assigned_identity_when_user_id_is_missing()
    {
        var interceptor = CreateInterceptor("Authentication=Active Directory Managed Identity");

        Assert.NotNull(interceptor);
        Assert.True(interceptor!.UsesSystemAssignedIdentity);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("\" \"")]
    public void Factory_rejects_invalid_user_id_in_managed_identity_mode(string userId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateInterceptor($"Authentication=Active Directory Managed Identity;User Id={userId}"));

        Assert.Equal("The catalog SQL managed identity configuration is invalid.", exception.Message);
    }

    [Fact]
    public void Factory_does_not_expose_malformed_connection_configuration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateInterceptor("secret-sentinel-key=value"));
        Assert.Equal("The catalog SQL connection configuration is invalid.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ConnectionCreated_removes_authentication_and_preserves_connection_properties()
    {
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(
            new RecordingCredential(),
            ClientId);
        using var connection = new SqlConnection(
            $"Server=tcp:catalog.database.windows.net,1433;Initial Catalog=Catalog;Encrypt=True;TrustServerCertificate=False;" +
            $"Connect Timeout=25;Connect Retry Count=2;Connect Retry Interval=4;Authentication=Active Directory Managed Identity;User Id={ClientId}");

        interceptor.ConnectionCreated(default!, connection);

        var actual = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("tcp:catalog.database.windows.net,1433", actual.DataSource);
        Assert.Equal("Catalog", actual.InitialCatalog);
        Assert.True(actual.Encrypt);
        Assert.False(actual.TrustServerCertificate);
        Assert.Equal(25, actual.ConnectTimeout);
        Assert.Equal(2, actual.ConnectRetryCount);
        Assert.Equal(4, actual.ConnectRetryInterval);
        Assert.Equal(ClientId, actual.UserID, ignoreCase: true);
        Assert.Equal(SqlAuthenticationMethod.NotSpecified, actual.Authentication);
        Assert.NotNull(connection.AccessTokenCallback);
    }

    [Fact]
    public async Task Callback_validates_public_sql_scope_and_identity_and_preserves_expiry()
    {
        var credential = new RecordingCredential();
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(credential, ClientId);
        using var connection = CreateManagedConnection(interceptor);

        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(10);
        credential.Token = new AccessToken("token-value", expiresOn);
        var token = await connection.AccessTokenCallback!(
            Parameters(ClientId, "https://database.windows.net/"), CancellationToken.None);

        Assert.Equal("token-value", token.AccessToken);
        Assert.Equal(expiresOn, token.ExpiresOn);
        Assert.Equal("https://database.windows.net/.default", credential.LastContext!.Value.Scopes.Single());
    }

    [Theory]
    [InlineData(OtherClientId, "https://database.windows.net/")]
    [InlineData(ClientId, "https://management.azure.com/")]
    [InlineData(ClientId, "http://database.windows.net/")]
    [InlineData(ClientId, "https://database.windows.net.evil.example/")]
    [InlineData("not-an-identity", "https://database.windows.net/")]
    [InlineData(ClientId, "https://user:secret@database.windows.net/")]
    [InlineData(ClientId, "https://database.windows.net/?secret=value")]
    [InlineData(ClientId, "https://database.windows.net/#fragment")]
    [InlineData(ClientId, "https://database.windows.net:444/")]
    public async Task Callback_rejects_mutated_identity_or_non_public_sql_scope(string userId, string resource)
    {
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(
            new RecordingCredential { Token = FutureToken() },
            ClientId);
        using var connection = CreateManagedConnection(interceptor);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.AccessTokenCallback!(Parameters(userId, resource), CancellationToken.None));

        Assert.Equal("The catalog SQL managed identity authentication parameters are invalid.", exception.Message);
    }

    [Fact]
    public async Task System_assigned_callback_rejects_user_assigned_identity_instead_of_switching()
    {
        var credential = new RecordingCredential();
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(credential, null);
        using var connection = CreateManagedConnection(interceptor, clientId: null);
        await connection.AccessTokenCallback!(Parameters("", "https://database.windows.net/"), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.AccessTokenCallback!(Parameters(ClientId, "https://database.windows.net/"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.AccessTokenCallback!(Parameters(" ", "https://database.windows.net/"), CancellationToken.None));
    }

    [Fact]
    public async Task Callback_honors_cancellation_and_rejects_expired_token()
    {
        var credential = new RecordingCredential { Token = new AccessToken("expired", DateTimeOffset.UtcNow.AddMinutes(-1)) };
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(credential, ClientId);
        using var connection = CreateManagedConnection(interceptor);

        var expired = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.AccessTokenCallback!(Parameters(ClientId, "https://database.windows.net/"), CancellationToken.None));
        Assert.Equal("The catalog SQL managed identity token is expired.", expired.Message);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            connection.AccessTokenCallback!(Parameters(ClientId, "https://database.windows.net/"), cancellation.Token));
        // The interceptor rejects pre-cancellation before calling the credential again.
        Assert.Equal(CancellationToken.None, credential.LastCancellationToken);
    }

    [Fact]
    public void ConnectionOpening_fails_safely_when_connection_identity_is_mutated()
    {
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(
            new RecordingCredential { Token = FutureToken() },
            ClientId);
        using var connection = CreateManagedConnection(interceptor);
        connection.ConnectionString = $"Server=catalog.database.windows.net;Database=Catalog;User Id={OtherClientId}";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            interceptor.ConnectionOpening(connection, default!, default));

        Assert.Equal("The catalog SQL managed identity connection identity is invalid.", exception.Message);
    }

    [Fact]
    public void Non_managed_identity_connections_are_unchanged()
    {
        var interceptor = new CatalogSqlManagedIdentityConnectionInterceptor(
            new RecordingCredential(),
            ClientId);
        using var connection = new SqlConnection("Server=catalog.database.windows.net;Database=Catalog;Authentication=Active Directory Default");
        var original = connection.ConnectionString;

        interceptor.ConnectionCreated(default!, connection);

        Assert.Equal(original, connection.ConnectionString);
        Assert.Null(connection.AccessTokenCallback);
    }

    [Fact]
    public void Sqlite_is_unchanged_when_optional_sql_server_configuration_is_used()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Catalog"] = "Data Source=:memory:"
        }).Build();
        var callbackCount = 0;

        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddCatalogDbContext(configuration, _ => callbackCount++)
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(0, callbackCount);
    }

    private static CatalogSqlManagedIdentityConnectionInterceptor? CreateInterceptor(string authentication)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:Catalog"] = $"Server=catalog.database.windows.net;Database=Catalog;{authentication}"
        }).Build();

        return CatalogSqlManagedIdentityConnectionInterceptor.TryCreate(configuration);
    }

    // Test-only adapter for the pinned driver's non-public DTO constructor; production uses
    // only the public callback API. The actual driver boundary is exercised by the live rehearsal.
    private static SqlAuthenticationParameters Parameters(string userId, string resource) =>
        (SqlAuthenticationParameters)typeof(SqlAuthenticationParameters)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(SqlAuthenticationMethod), typeof(string), typeof(string), typeof(string), typeof(string),
                    typeof(string), typeof(string), typeof(Guid), typeof(int)], null)!
            .Invoke([SqlAuthenticationMethod.NotSpecified, "catalog.database.windows.net", "Catalog", resource,
                "https://login.microsoftonline.com/", userId, null, Guid.NewGuid(), 30]);

    private static SqlConnection CreateManagedConnection(
        CatalogSqlManagedIdentityConnectionInterceptor interceptor, string? clientId = ClientId)
    {
        var connection = new SqlConnection(
            $"Server=catalog.database.windows.net;Database=Catalog;Authentication=Active Directory Managed Identity;User Id={clientId}");
        interceptor.ConnectionCreated(default!, connection);
        return connection;
    }

    private static AccessToken FutureToken() => new("token", DateTimeOffset.UtcNow.AddMinutes(10));

    private sealed class RecordingCredential : TokenCredential
    {
        public AccessToken Token { get; set; } = FutureToken();
        public TokenRequestContext? LastContext { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastContext = requestContext;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Token;
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastContext = requestContext;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Token);
        }
    }
}
