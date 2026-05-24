using Microsoft.AspNetCore.Authentication;

namespace Elsa.Platform.Api.Authentication;

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

    public static bool IsWorkspaceApiMutation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/workspaces") &&
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

        var host = GetEffectiveHost(request);
        return HeaderComparer.Equals(uri.Scheme, GetEffectiveScheme(request)) &&
               HeaderComparer.Equals(uri.Host, host.Host) &&
               uri.Port == GetPort(request, host);
    }

    private static string GetEffectiveScheme(HttpRequest request) =>
        GetForwardedHeaderValue(request, "X-Forwarded-Proto") ?? request.Scheme;

    private static HostString GetEffectiveHost(HttpRequest request)
    {
        var forwardedHost = GetForwardedHeaderValue(request, "X-Forwarded-Host");
        return HostString.FromUriComponent(forwardedHost ?? request.Host.ToUriComponent());
    }

    private static string? GetForwardedHeaderValue(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
            return null;

        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var commaIndex = value.IndexOf(',');
        return (commaIndex >= 0 ? value[..commaIndex] : value).Trim();
    }

    private static int GetPort(HttpRequest request, HostString host)
    {
        if (host.Port is { } port)
            return port;

        return HeaderComparer.Equals(GetEffectiveScheme(request), "https") ? 443 : 80;
    }
}

public sealed class AdminDashboardRequestForgeryMiddleware(
    RequestDelegate next,
    AdminApiKeyValidator apiKeyValidator)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (AdminDashboardRequestForgeryGuard.IsAdminApiMutation(context.Request))
        {
            if (AdminDashboardRequestForgeryGuard.HasValidApiKey(context.Request, apiKeyValidator))
            {
                await next(context);
                return;
            }

            var customerCookieResult = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
            if (!customerCookieResult.Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!AdminDashboardRequestForgeryGuard.IsSameOriginBrowserRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
            return;
        }

        if (!AdminDashboardRequestForgeryGuard.IsWorkspaceApiMutation(context.Request) ||
            PlatformIdentityReader.HasBearerToken(context.Request.HttpContext))
        {
            await next(context);
            return;
        }

        var customerResult = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!customerResult.Succeeded)
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
