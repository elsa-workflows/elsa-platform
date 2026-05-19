using System.Net;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminDashboardAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AdminDashboardAuthenticationDefaults.LoginPath, async (HttpContext context) =>
        {
            var authentication = await context.AuthenticateAsync(AdminDashboardAuthenticationDefaults.Scheme);
            if (authentication.Succeeded)
                return Results.Redirect(GetSafeReturnUrl(context.Request.Query["returnUrl"]));

            return Results.Content(RenderLoginPage(context.Request.Query["returnUrl"], null), "text/html");
        }).AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LoginPath, async (HttpContext context, AdminApiKeyValidator validator, AdminDashboardLoginThrottle throttle, TimeProvider timeProvider) =>
        {
            var throttleDecision = throttle.Check(context);
            if (throttleDecision.IsThrottled)
            {
                var remainingSeconds = Math.Ceiling((throttleDecision.RetryAfter!.Value - timeProvider.GetUtcNow()).TotalSeconds);
                context.Response.Headers.RetryAfter = Math.Max(0, (int)remainingSeconds).ToString();
                var throttledForm = context.Request.HasFormContentType
                    ? await context.Request.ReadFormAsync(context.RequestAborted)
                    : null;

                return Results.Content(
                    RenderLoginPage(throttledForm?["returnUrl"].FirstOrDefault(), "Too many failed attempts. Try again later."),
                    "text/html",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var returnUrl = form["returnUrl"].FirstOrDefault();
            var apiKey = form["apiKey"].FirstOrDefault();

            if (!validator.IsValid(apiKey))
            {
                throttle.RecordFailure(throttleDecision.ClientKey);
                return Results.Content(
                    RenderLoginPage(returnUrl, "The admin key was not accepted."),
                    "text/html",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var principal = AdminPrincipalFactory.Create("admin-dashboard", AdminDashboardAuthenticationDefaults.Scheme);
            await context.SignInAsync(
                AdminDashboardAuthenticationDefaults.Scheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    IssuedUtc = timeProvider.GetUtcNow()
                });

            throttle.Clear(throttleDecision.ClientKey);
            return Results.Redirect(GetSafeReturnUrl(returnUrl));
        }).AllowAnonymous();

        endpoints.MapPost(AdminDashboardAuthenticationDefaults.LogoutPath, async (HttpContext context) =>
        {
            if (!AdminDashboardRequestForgeryGuard.IsSameOriginPost(context.Request))
                return Results.Forbid();

            await context.SignOutAsync(AdminDashboardAuthenticationDefaults.Scheme);
            return Results.Redirect(AdminDashboardAuthenticationDefaults.LoginPath);
        }).AllowAnonymous();

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

    private static string RenderLoginPage(string? returnUrl, string? error)
    {
        var safeReturnUrl = WebUtility.HtmlEncode(GetSafeReturnUrl(returnUrl));
        var errorMarkup = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"""<p class="error">{WebUtility.HtmlEncode(error)}</p>""";

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Elsa Catalog Admin</title>
  <style>
    :root { color-scheme: light dark; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
    body { min-height: 100vh; margin: 0; display: grid; place-items: center; background: Canvas; color: CanvasText; }
    main { width: min(100% - 32px, 360px); }
    h1 { margin: 0 0 8px; font-size: 1.25rem; font-weight: 650; letter-spacing: 0; }
    p { margin: 0 0 20px; color: color-mix(in srgb, CanvasText 70%, Canvas); font-size: .925rem; line-height: 1.5; }
    form { display: grid; gap: 12px; }
    label { display: grid; gap: 6px; font-size: .875rem; font-weight: 520; }
    input { box-sizing: border-box; width: 100%; border: 1px solid color-mix(in srgb, CanvasText 20%, Canvas); border-radius: 8px; padding: 10px 12px; font: inherit; background: Canvas; color: CanvasText; }
    button { border: 0; border-radius: 8px; padding: 10px 12px; font: inherit; font-weight: 620; color: white; background: #111827; cursor: pointer; }
    .error { color: #b91c1c; margin-bottom: 4px; }
    @media (prefers-color-scheme: dark) { button { background: #f9fafb; color: #111827; } .error { color: #fca5a5; } }
  </style>
</head>
<body>
  <main>
    <h1>Elsa Catalog Admin</h1>
    <p>Enter the configured admin key to continue.</p>
    {{errorMarkup}}
    <form method="post" action="{{AdminDashboardAuthenticationDefaults.LoginPath}}">
      <input type="hidden" name="returnUrl" value="{{safeReturnUrl}}">
      <label>
        Admin key
        <input name="apiKey" type="password" autocomplete="current-password" autofocus required>
      </label>
      <button type="submit">Sign in</button>
    </form>
  </main>
</body>
</html>
""";
    }
}
