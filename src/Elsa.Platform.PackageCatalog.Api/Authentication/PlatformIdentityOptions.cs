namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class PlatformIdentityDefaults
{
    public const string Scheme = "PlatformOidcJwt";
    public const string ConfigurationSection = "Authentication:PlatformIdentity";
}

public sealed class PlatformIdentityOptions
{
    public PlatformIdentityProviderKind Provider { get; init; } = PlatformIdentityProviderKind.GenericOidc;
    public string? Authority { get; init; }
    public string? Audience { get; init; }
    public string? Issuer { get; init; }
    public string? SymmetricSigningKey { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string[] Scopes { get; init; } = ["openid", "profile", "email"];
    public string? RedirectUri { get; init; }
    public string? PostLogoutRedirectUri { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
    public PlatformIdentityClaimOptions Claims { get; init; } = new();

    public bool IsCustomerLoginConfigured =>
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(ClientId);
}

public sealed class PlatformIdentityClaimOptions
{
    public string Subject { get; init; } = "sub";
    public string[] DisplayName { get; init; } = ["name"];
    public string[] Email { get; init; } = ["email"];
}

public enum PlatformIdentityProviderKind
{
    GenericOidc,
    MicrosoftEntra,
    Auth0,
    Keycloak,
    Custom
}
