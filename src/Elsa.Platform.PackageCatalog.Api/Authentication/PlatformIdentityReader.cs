using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class PlatformIdentityReader(IOptions<PlatformIdentityOptions> options) : IWorkspaceIdentityReader
{
    private readonly PlatformIdentityOptions _options = options.Value;

    public static bool HasBearerToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.FirstOrDefault();
        return authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(PlatformIdentityDefaults.Scheme);
        var user = result.Succeeded ? result.Principal : context.User;
        return PlatformClaimsIdentityMapper.ToTrustedWorkspaceIdentity(user, _options);
    }
}

internal static class PlatformClaimsIdentityMapper
{
    public static TrustedWorkspaceIdentity? ToTrustedWorkspaceIdentity(ClaimsPrincipal? user, PlatformIdentityOptions options)
    {
        if (user?.Identity is not { IsAuthenticated: true })
            return null;

        var issuer = ClaimValue(user, JwtRegisteredClaimNames.Iss)
            ?? options.Issuer
            ?? options.Authority;
        var subject = ClaimValue(user, options.Claims.Subject)
            ?? ClaimValue(user, ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return null;

        return new TrustedWorkspaceIdentity(
            issuer,
            subject,
            FirstClaimValue(user, options.Claims.DisplayName)
                ?? ClaimValue(user, JwtRegisteredClaimNames.Name)
                ?? ClaimValue(user, ClaimTypes.Name),
            FirstClaimValue(user, options.Claims.Email)
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

public sealed class CompositeWorkspaceIdentityReader(IEnumerable<IWorkspaceIdentityReader> readers) : IWorkspaceIdentityReader
{
    private readonly IReadOnlyList<IWorkspaceIdentityReader> _readers = readers.ToArray();

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        foreach (var reader in _readers)
        {
            var identity = await reader.ReadAsync(context);
            if (identity is not null)
                return identity;
            if (reader is PlatformIdentityReader && PlatformIdentityReader.HasBearerToken(context))
                return null;
        }

        return null;
    }
}
