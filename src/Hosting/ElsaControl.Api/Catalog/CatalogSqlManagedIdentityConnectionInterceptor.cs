using System.Data.Common;
using Azure.Core;
using Azure.Identity;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace ElsaControl.Api.Catalog;

/// <summary>
/// Applies a stable access-token callback to catalog connections that explicitly opt in to
/// SqlClient managed-identity authentication.
/// </summary>
/// <remarks>
/// The callback is attached after EF creates each connection, keeping the Azure identity
/// dependency in the host while leaving the provider-neutral catalog package unchanged.
/// EF connection interception: https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
/// SqlClient callback contract: https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.accesstokencallback
/// </remarks>
internal sealed class CatalogSqlManagedIdentityConnectionInterceptor : DbConnectionInterceptor
{
    private const string AzureSqlScope = "https://database.windows.net/.default";
    private readonly TokenCredential _credential;
    private readonly Guid? _userAssignedClientId;
    private readonly Func<SqlAuthenticationParameters, CancellationToken, Task<SqlAuthenticationToken>> _accessTokenCallback;
    private readonly ConditionalWeakTable<SqlConnection, ConnectionBinding> _connections = new();

    internal CatalogSqlManagedIdentityConnectionInterceptor(TokenCredential credential, string? userAssignedClientId)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _userAssignedClientId = ParseUserAssignedClientId(userAssignedClientId);
        _accessTokenCallback = AcquireTokenAsync;
    }

    internal bool UsesSystemAssignedIdentity => _userAssignedClientId is null;

    internal static CatalogSqlManagedIdentityConnectionInterceptor? TryCreate(IConfiguration configuration)
    {
        var databaseOptions = configuration
            .GetSection(CatalogDatabaseOptions.SectionName)
            .Get<CatalogDatabaseOptions>() ?? new CatalogDatabaseOptions();
        if (databaseOptions.Provider != CatalogDatabaseProvider.SqlServer)
            return null;

        var connectionString = configuration.GetConnectionString("Catalog");
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The catalog SQL connection configuration is invalid.");
        }
        if (!IsManagedIdentityAuthentication(builder.Authentication))
            return null;

        var userAssignedClientId = string.IsNullOrEmpty(builder.UserID) ? null : builder.UserID;
        var parsedClientId = ParseUserAssignedClientId(userAssignedClientId);
        var identity = parsedClientId is { } clientId
            ? ManagedIdentityId.FromUserAssignedClientId(clientId.ToString("D"))
            : ManagedIdentityId.SystemAssigned;
        var credential = new ManagedIdentityCredential(new ManagedIdentityCredentialOptions(identity)
        {
            RetryPolicy = new ManagedIdentityProbeRetryPolicy()
        });
        return new CatalogSqlManagedIdentityConnectionInterceptor(credential, userAssignedClientId);
    }

    public override DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, DbConnection result)
    {
        if (result is not SqlConnection connection || !TryGetCatalogConnection(connection, out var builder))
            return result;

        ValidateIdentity(builder.UserID);

        // SqlClient rejects AccessTokenCallback when a token-owning Authentication keyword
        // remains on the connection. Remove only that
        // keyword from the actual EF-created connection; all TLS, retry, server, database,
        // and User Id settings remain in the connection string.
        builder.Remove("Authentication");
        connection.ConnectionString = builder.ConnectionString;
        connection.AccessTokenCallback = _accessTokenCallback;
        _connections.Remove(connection);
        _connections.Add(connection, new ConnectionBinding(connection.ConnectionString));
        return result;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ValidateBeforeOpen(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBeforeOpen(connection);
        return ValueTask.FromResult(result);
    }

    private void ValidateBeforeOpen(DbConnection connection)
    {
        if (connection is not SqlConnection sqlConnection || !_connections.TryGetValue(sqlConnection, out var binding))
            return;

        if (!ReferenceEquals(sqlConnection.AccessTokenCallback, _accessTokenCallback))
            throw InvalidConnectionIdentity();

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString);
        }
        catch (ArgumentException)
        {
            throw InvalidConnectionIdentity();
        }

        if (builder.ContainsKey("Authentication") || !MatchesIdentity(builder.UserID) ||
            !string.Equals(sqlConnection.ConnectionString, binding.ConnectionString, StringComparison.Ordinal))
            throw InvalidConnectionIdentity();
    }

    private async Task<SqlAuthenticationToken> AcquireTokenAsync(
        SqlAuthenticationParameters parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MatchesIdentity(parameters.UserId) || !IsPublicAzureSqlResource(parameters.Resource))
            throw InvalidAuthenticationParameters();

        var token = await _credential.GetTokenAsync(
                new TokenRequestContext([AzureSqlScope]),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token.Token) || token.ExpiresOn <= DateTimeOffset.UtcNow)
            throw ExpiredToken();

        return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
    }

    private static bool TryGetCatalogConnection(
        SqlConnection connection,
        out SqlConnectionStringBuilder builder)
    {
        try
        {
            builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        }
        catch (ArgumentException)
        {
            builder = null!;
            return false;
        }

        return IsManagedIdentityAuthentication(builder.Authentication);
    }

    private static bool IsManagedIdentityAuthentication(SqlAuthenticationMethod authentication) =>
        authentication == SqlAuthenticationMethod.ActiveDirectoryManagedIdentity ||
        authentication == SqlAuthenticationMethod.ActiveDirectoryMSI;

    private bool MatchesIdentity(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return _userAssignedClientId is null;
        if (!Guid.TryParse(userId, out var parsed))
            return false;
        return parsed == _userAssignedClientId;
    }

    private void ValidateIdentity(string? userId)
    {
        if (!MatchesIdentity(userId))
            throw InvalidConnectionIdentity();
    }

    private static Guid? ParseUserAssignedClientId(string? userAssignedClientId)
    {
        if (string.IsNullOrEmpty(userAssignedClientId))
            return null;
        if (!Guid.TryParse(userAssignedClientId, out var clientId) || clientId == Guid.Empty)
            throw new InvalidOperationException("The catalog SQL managed identity configuration is invalid.");
        return clientId;
    }

    private static bool IsPublicAzureSqlResource(string? resource)
    {
        if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "database.windows.net", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;
        return true;
    }

    private static InvalidOperationException InvalidConnectionIdentity() =>
        new("The catalog SQL managed identity connection identity is invalid.");

    private static InvalidOperationException InvalidAuthenticationParameters() =>
        new("The catalog SQL managed identity authentication parameters are invalid.");

    private static InvalidOperationException ExpiredToken() =>
        new("The catalog SQL managed identity token is expired.");

    private sealed record ConnectionBinding(string ConnectionString);
}
