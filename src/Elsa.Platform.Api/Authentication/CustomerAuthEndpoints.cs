using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Authentication;

public static class CustomerAuthEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Customer Authentication");

        group.MapGet("/session", async (
            HttpContext context,
            IOptions<PlatformIdentityOptions> options,
            CustomerSessionIdentityReader sessionIdentityReader) =>
        {
            var identity = await sessionIdentityReader.ReadAsync(context);
            return Results.Ok(new CustomerAuthSessionResponse(
                options.Value.IsCustomerLoginConfigured,
                identity is not null,
                identity?.DisplayName,
                identity?.Email,
                CustomerAuthenticationDefaults.LoginPath,
                CustomerAuthenticationDefaults.LogoutPath));
        }).AllowAnonymous();

        group.MapGet("/login", async (
            IOptions<PlatformIdentityOptions> options,
            IOptionsMonitor<OpenIdConnectOptions> oidcOptionsMonitor,
            string? returnUrl) =>
        {
            if (!options.Value.IsCustomerLoginConfigured)
            {
                return Results.Problem(
                    title: "Customer login is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var oidcOptions = oidcOptionsMonitor.Get(CustomerAuthenticationDefaults.OidcScheme);
            try
            {
                await oidcOptions.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Identity provider is currently unavailable.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var redirectUri = GetSafeReturnUrl(returnUrl);
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = redirectUri },
                [CustomerAuthenticationDefaults.OidcScheme]);
        }).AllowAnonymous();

        group.MapPost("/logout", SignOutAsync).AllowAnonymous();
        group.MapPost("/sign-out", SignOutAsync).AllowAnonymous();

        return endpoints;
    }

    public static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return CustomerAuthenticationDefaults.DefaultReturnPath;

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) &&
            returnUrl.StartsWith("/", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
            !returnUrl.StartsWith(AdminDashboardAuthenticationDefaults.LoginPath, StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith(AdminDashboardAuthenticationDefaults.LogoutPath, StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith(CustomerAuthenticationDefaults.LoginPath, StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith(CustomerAuthenticationDefaults.LogoutPath, StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith("/api/auth/sign-in", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith("/api/auth/sign-out", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith(CustomerAuthenticationDefaults.CallbackPath, StringComparison.OrdinalIgnoreCase))
            return uri.OriginalString;

        return CustomerAuthenticationDefaults.DefaultReturnPath;
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        IOptions<PlatformIdentityOptions> options,
        string? returnUrl)
    {
        if (!AdminDashboardRequestForgeryGuard.IsSameOriginBrowserRequest(context.Request))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var redirectUri = GetSafeReturnUrl(returnUrl);
        await context.SignOutAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!options.Value.IsCustomerLoginConfigured)
            return Results.NoContent();

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = redirectUri },
            [CustomerAuthenticationDefaults.OidcScheme]);
    }
}

public sealed record CustomerAuthSessionResponse(
    bool LoginEnabled,
    bool Authenticated,
    string? DisplayName,
    string? Email,
    string LoginPath,
    string LogoutPath);
