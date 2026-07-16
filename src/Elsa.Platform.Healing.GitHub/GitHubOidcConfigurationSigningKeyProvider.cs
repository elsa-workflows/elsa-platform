using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Platform.Healing.GitHub;

/// <summary>
/// Resolves GitHub Actions signing keys only through the official HTTPS discovery document.
/// ConfigurationManager provides bounded automatic metadata/JWKS refresh and last-known-good behavior.
/// </summary>
public sealed class GitHubOidcConfigurationSigningKeyProvider : IGitHubOidcSigningKeyProvider
{
    public const string DiscoveryEndpoint =
        GitHubWorkloadIdentityValidator.GitHubIssuer + "/.well-known/openid-configuration";

    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public GitHubOidcConfigurationSigningKeyProvider(HttpClient httpClient)
        : this(CreateConfigurationManager(httpClient))
    {
    }

    public GitHubOidcConfigurationSigningKeyProvider(
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
    }

    public async ValueTask<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        if (!string.Equals(configuration.Issuer, GitHubWorkloadIdentityValidator.GitHubIssuer, StringComparison.Ordinal) ||
            configuration.SigningKeys.Count == 0)
            return [];
        return configuration.SigningKeys.ToArray();
    }

    public void RequestRefresh() => _configurationManager.RequestRefresh();

    private static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        var retriever = new HttpDocumentRetriever(httpClient) { RequireHttps = true };
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            DiscoveryEndpoint,
            new OpenIdConnectConfigurationRetriever(),
            retriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromHours(12),
            RefreshInterval = TimeSpan.FromMinutes(5)
        };
    }
}
