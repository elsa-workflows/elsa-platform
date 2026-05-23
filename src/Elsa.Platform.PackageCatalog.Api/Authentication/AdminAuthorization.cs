using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminAuthorization
{
    public const string Policy = "AdminApi";
    public const string PlatformAdminRole = "platform_admin";

    public static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(Policy, policy =>
            {
                policy.AuthenticationSchemes.Add(ApiKeyAuthenticationDefaults.Scheme);
                policy.AuthenticationSchemes.Add(CustomerAuthenticationDefaults.CookieScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasApiKeyIdentity(context.User) ||
                    HasPlatformAdminRole(context.User));
            }));
        return services;
    }

    public static bool HasPlatformAdminRole(ClaimsPrincipal principal) =>
        principal.Claims.Any(claim =>
            IsRoleClaim(claim.Type) &&
            string.Equals(claim.Value, PlatformAdminRole, StringComparison.OrdinalIgnoreCase));

    private static bool HasApiKeyIdentity(ClaimsPrincipal principal) =>
        principal.Identities.Any(identity =>
            identity.IsAuthenticated &&
            string.Equals(identity.AuthenticationType, ApiKeyAuthenticationDefaults.Scheme, StringComparison.Ordinal));

    private static bool IsRoleClaim(string type) =>
        string.Equals(type, ClaimTypes.Role, StringComparison.Ordinal) ||
        string.Equals(type, "role", StringComparison.Ordinal) ||
        string.Equals(type, "roles", StringComparison.Ordinal);
}
