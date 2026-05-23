using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class PlatformIdentityConfigurationValidator(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<PlatformIdentityOptions> options) : IHostedService
{
    private readonly PlatformIdentityOptions _options = options.Value;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = Validate().ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException($"Platform identity configuration is invalid: {string.Join(" ", errors)}");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IEnumerable<string> Validate()
    {
        var isProduction = environment.IsProduction();
        if (isProduction && !_options.RequireHttpsMetadata)
            yield return "Authentication:PlatformIdentity:RequireHttpsMetadata must be true in Production.";

        if (isProduction && configuration.GetValue<bool>(TrustedHeaderWorkspaceIdentityReader.EnabledConfigurationKey))
            yield return "Authentication:WorkspaceTrustedHeaders:Enabled must be false in Production.";

        if (!string.IsNullOrWhiteSpace(_options.ClientId) && string.IsNullOrWhiteSpace(_options.Authority))
            yield return "Authentication:PlatformIdentity:Authority is required when ClientId is configured.";

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret) && string.IsNullOrWhiteSpace(_options.ClientId))
            yield return "Authentication:PlatformIdentity:ClientId is required when ClientSecret is configured.";

        if (_options.Provider == PlatformIdentityProviderKind.Keycloak)
        {
            if (string.IsNullOrWhiteSpace(_options.Authority))
                yield return "Authentication:PlatformIdentity:Authority is required for Keycloak.";
            if (string.IsNullOrWhiteSpace(_options.ClientId))
                yield return "Authentication:PlatformIdentity:ClientId is required for Keycloak.";
            if (isProduction && string.IsNullOrWhiteSpace(_options.ClientSecret))
                yield return "Authentication:PlatformIdentity:ClientSecret is required for Keycloak in Production.";
        }
    }
}
