namespace Elsa.Platform.Api.Authentication;

public static class AdminDashboardAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AdminDashboardAuthenticationDefaults.LoginPath, (HttpContext context) =>
            Results.Redirect(GetSignInUrl(context.Request.Query["returnUrl"].FirstOrDefault())))
            .AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LoginPath, async (HttpContext context) =>
            Results.Redirect(GetSignInUrl(await GetFormReturnUrlAsync(context))))
            .AllowAnonymous()
            .DisableAntiforgery();

        endpoints.MapGet(AdminDashboardAuthenticationDefaults.LogoutPath, () =>
            Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LogoutPath, () =>
            Results.Redirect(
                $"{CustomerAuthenticationDefaults.LogoutPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}",
                preserveMethod: true))
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
            returnUrl.StartsWith(AdminDashboardAuthenticationDefaults.LoginPath, StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith(AdminDashboardAuthenticationDefaults.LogoutPath, StringComparison.OrdinalIgnoreCase))
        {
            return AdminDashboardAuthenticationDefaults.DefaultReturnPath;
        }

        return returnUrl;
    }

    private static string GetSignInUrl(string? returnUrl) =>
        $"{CustomerAuthenticationDefaults.LoginPath}?returnUrl={Uri.EscapeDataString(GetSafeReturnUrl(returnUrl))}";

    private static async Task<string?> GetFormReturnUrlAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
            return context.Request.Query["returnUrl"].FirstOrDefault();

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return form["returnUrl"].FirstOrDefault();
    }
}
