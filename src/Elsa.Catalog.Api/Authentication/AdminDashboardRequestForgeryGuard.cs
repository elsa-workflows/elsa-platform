using Microsoft.AspNetCore.Authentication;

namespace Elsa.Catalog.Api.Authentication;

public static class AdminDashboardRequestForgeryGuard
{
    private static readonly StringComparer HeaderComparer = StringComparer.OrdinalIgnoreCase;

    public static bool IsSameOriginPost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && IsSameOriginBrowserRequest(request);

    public static bool IsAdminApiMutation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/admin") &&
        (HttpMethods.IsPost(request.Method) ||
         HttpMethods.IsPut(request.Method) ||
         HttpMethods.IsPatch(request.Method) ||
         HttpMethods.IsDelete(request.Method));

    public static bool IsSameOriginBrowserRequest(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Origin", out var origins))
            return origins.Any(value => IsSameOrigin(value, request));

        if (request.Headers.TryGetValue("Referer", out var referers))
            return referers.Any(value => IsSameOrigin(value, request));

        return false;
    }

    public static bool HasValidApiKey(HttpRequest request, AdminApiKeyValidator validator) =>
        request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values) &&
        values.Any(validator.IsValid);

    private static bool IsSameOrigin(string? value, HttpRequest request)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return HeaderComparer.Equals(uri.Scheme, request.Scheme) &&
               HeaderComparer.Equals(uri.Host, request.Host.Host) &&
               uri.Port == GetPort(request);
    }

    private static int GetPort(HttpRequest request)
    {
        if (request.Host.Port is { } port)
            return port;

        return HeaderComparer.Equals(request.Scheme, "https") ? 443 : 80;
    }
}

public sealed class AdminDashboardRequestForgeryMiddleware(
    RequestDelegate next,
    AdminApiKeyValidator apiKeyValidator)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!AdminDashboardRequestForgeryGuard.IsAdminApiMutation(context.Request) ||
            AdminDashboardRequestForgeryGuard.HasValidApiKey(context.Request, apiKeyValidator))
        {
            await next(context);
            return;
        }

        var cookieResult = await context.AuthenticateAsync(AdminDashboardAuthenticationDefaults.Scheme);
        if (!cookieResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (!AdminDashboardRequestForgeryGuard.IsSameOriginBrowserRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

public static class AdminDashboardRequestForgeryMiddlewareExtensions
{
    public static IApplicationBuilder UseAdminDashboardRequestForgeryGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<AdminDashboardRequestForgeryMiddleware>();
}
