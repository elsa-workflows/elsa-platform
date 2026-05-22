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
    public bool RequireHttpsMetadata { get; init; } = true;
    public PlatformIdentityClaimOptions Claims { get; init; } = new();
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
