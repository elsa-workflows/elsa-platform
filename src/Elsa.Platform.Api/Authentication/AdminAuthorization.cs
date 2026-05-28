using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Authentication;

public static class AdminAuthorization
{
    public const string Policy = "AdminApi";
    public const string PlatformAdminRole = "platform_admin";

    public static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services.AddOptions<AdminAuthorizationOptions>()
            .BindConfiguration(AdminAuthorizationOptions.ConfigurationSection);
        services.AddSingleton<IAuthorizationHandler, AdminApiAuthorizationHandler>();
        services.AddAuthorization(options =>
            options.AddPolicy(Policy, policy =>
            {
                policy.AuthenticationSchemes.Add(ApiKeyAuthenticationDefaults.Scheme);
                policy.AuthenticationSchemes.Add(CustomerAuthenticationDefaults.CookieScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new AdminApiRequirement());
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

    private static bool HasAuthenticatedIdentity(ClaimsPrincipal principal) =>
        principal.Identities.Any(identity => identity.IsAuthenticated);

    private static bool IsRoleClaim(string type) =>
        string.Equals(type, ClaimTypes.Role, StringComparison.Ordinal) ||
        string.Equals(type, "role", StringComparison.Ordinal) ||
        string.Equals(type, "roles", StringComparison.Ordinal);

    private sealed class AdminApiRequirement : IAuthorizationRequirement;

    private sealed class AdminApiAuthorizationHandler(
        IOptions<AdminAuthorizationOptions> options,
        IWebHostEnvironment environment) : AuthorizationHandler<AdminApiRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminApiRequirement requirement)
        {
            if (HasApiKeyIdentity(context.User) || HasPlatformAdminRole(context.User))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            if (!environment.IsProduction() &&
                options.Value.AllowAuthenticatedCustomerSession &&
                HasAuthenticatedIdentity(context.User))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}

public sealed class AdminAuthorizationOptions
{
    public const string ConfigurationSection = "Authentication:Admin";

    public bool AllowAuthenticatedCustomerSession { get; init; }
}
