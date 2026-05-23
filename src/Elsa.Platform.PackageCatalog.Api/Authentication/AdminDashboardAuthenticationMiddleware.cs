using Microsoft.AspNetCore.Authentication;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class AdminDashboardAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresDashboardAuthentication(context.Request.Path))
        {
            await next(context);
            return;
        }

        var apiKeyResult = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.Scheme);
        if (apiKeyResult.Succeeded && apiKeyResult.Principal is not null)
        {
            context.User = apiKeyResult.Principal;
            await next(context);
            return;
        }

        var customerResult = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (customerResult.Succeeded && customerResult.Principal is not null)
        {
            context.User = customerResult.Principal;
            await next(context);
            return;
        }

        if (IsBrowserNavigation(context.Request))
        {
            var returnUrl = Uri.EscapeDataString(context.Request.PathBase + context.Request.Path + context.Request.QueryString);
            context.Response.Redirect($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl={returnUrl}");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool RequiresDashboardAuthentication(PathString path) =>
        path.StartsWithSegments("/admin") &&
        !path.StartsWithSegments(AdminDashboardAuthenticationDefaults.LoginPath) &&
        !path.StartsWithSegments(AdminDashboardAuthenticationDefaults.LogoutPath);

    private static bool IsBrowserNavigation(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) &&
        request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
}

public static class AdminDashboardAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseAdminDashboardAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<AdminDashboardAuthenticationMiddleware>();
}
