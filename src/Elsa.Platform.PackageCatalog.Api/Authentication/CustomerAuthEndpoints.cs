using System.Security.Claims;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

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
        });

        group.MapGet("/login", (
            HttpContext context,
            IOptions<PlatformIdentityOptions> options,
            string? returnUrl) =>
        {
            if (!options.Value.IsCustomerLoginConfigured)
            {
                return Results.Problem(
                    title: "Customer login is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var redirectUri = SafeReturnUrl(returnUrl);
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = redirectUri },
                [CustomerAuthenticationDefaults.OidcScheme]);
        });

        group.MapPost("/logout", async (
            HttpContext context,
            IOptions<PlatformIdentityOptions> options,
            string? returnUrl) =>
        {
            var redirectUri = SafeReturnUrl(returnUrl);
            await context.SignOutAsync(CustomerAuthenticationDefaults.CookieScheme);
            if (!options.Value.IsCustomerLoginConfigured)
                return Results.NoContent();

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = redirectUri },
                [CustomerAuthenticationDefaults.OidcScheme]);
        });

        return endpoints;
    }

    private static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return CustomerAuthenticationDefaults.DefaultReturnPath;

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) && !returnUrl.StartsWith("//", StringComparison.Ordinal))
            return uri.OriginalString;

        return CustomerAuthenticationDefaults.DefaultReturnPath;
    }
}

public sealed record CustomerAuthSessionResponse(
    bool LoginEnabled,
    bool Authenticated,
    string? DisplayName,
    string? Email,
    string LoginPath,
    string LogoutPath);
