namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminDashboardAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AdminDashboardAuthenticationDefaults.LoginPath, (HttpContext context) =>
            Results.Redirect(GetSignInUrl(context.Request.Query["returnUrl"].FirstOrDefault())))
            .AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LoginPath, (HttpContext context) =>
            Results.Redirect(GetSignInUrl(context.Request.Form["returnUrl"].FirstOrDefault())))
            .AllowAnonymous()
            .DisableAntiforgery();

        endpoints.MapGet(AdminDashboardAuthenticationDefaults.LogoutPath, () =>
            Results.Redirect($"{CustomerAuthenticationDefaults.LogoutPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}"))
            .AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LogoutPath, () =>
            Results.Redirect($"{CustomerAuthenticationDefaults.LogoutPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}"))
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }

    public static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            !Uri.TryCreate(returnUrl, UriKind.Relative, out _) ||
            !returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith(AdminDashboardAuthenticationDefaults.LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            return AdminDashboardAuthenticationDefaults.DefaultReturnPath;
        }

        return returnUrl;
    }

    private static string GetSignInUrl(string? returnUrl) =>
        $"{CustomerAuthenticationDefaults.LoginPath}?returnUrl={Uri.EscapeDataString(GetSafeReturnUrl(returnUrl))}";
}
