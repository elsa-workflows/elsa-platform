using Microsoft.AspNetCore.Authorization;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminAuthorization
{
    public const string Policy = "AdminApi";

    public static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(Policy, policy =>
            {
                policy.AuthenticationSchemes.Add(ApiKeyAuthenticationDefaults.Scheme);
                policy.AuthenticationSchemes.Add(AdminDashboardAuthenticationDefaults.Scheme);
                policy.RequireAuthenticatedUser();
            }));
        return services;
    }
}
