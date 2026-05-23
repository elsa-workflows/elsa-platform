using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class CustomerSessionIdentityReader(IOptions<PlatformIdentityOptions> options) : IWorkspaceIdentityReader
{
    private readonly PlatformIdentityOptions _options = options.Value;

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!result.Succeeded || result.Principal?.Identity is not { IsAuthenticated: true })
            return null;

        var user = result.Principal;
        var issuer = ClaimValue(user, JwtRegisteredClaimNames.Iss)
            ?? _options.Issuer
            ?? _options.Authority;
        var subject = ClaimValue(user, _options.Claims.Subject)
            ?? ClaimValue(user, ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return null;

        return new TrustedWorkspaceIdentity(
            issuer,
            subject,
            FirstClaimValue(user, _options.Claims.DisplayName)
                ?? ClaimValue(user, JwtRegisteredClaimNames.Name)
                ?? ClaimValue(user, ClaimTypes.Name),
            FirstClaimValue(user, _options.Claims.Email)
                ?? ClaimValue(user, JwtRegisteredClaimNames.Email)
                ?? ClaimValue(user, ClaimTypes.Email));
    }

    private static string? FirstClaimValue(ClaimsPrincipal principal, IEnumerable<string> types)
    {
        foreach (var type in types)
        {
            var value = ClaimValue(principal, type);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ClaimValue(ClaimsPrincipal principal, string type) =>
        principal.FindFirst(type)?.Value;
}
