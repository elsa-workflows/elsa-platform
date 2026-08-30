using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Authentication;

public sealed class CustomerSessionIdentityReader(IOptions<ControlIdentityOptions> options) :
    IWorkspaceIdentityReader,
    IAuthenticatedControlSessionReader
{
    private readonly ControlIdentityOptions _options = options.Value;

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!result.Succeeded || result.Principal?.Identity is not { IsAuthenticated: true })
            return null;

        return ControlClaimsIdentityMapper.ToTrustedWorkspaceIdentity(result.Principal, _options);
    }

    public async ValueTask<AuthenticatedControlSession?> ReadAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        return ToAuthenticatedControlSession(result, _options);
    }

    internal static AuthenticatedControlSession? ToAuthenticatedControlSession(
        AuthenticateResult result,
        ControlIdentityOptions options)
    {
        if (!result.Succeeded || result.Principal is null)
            return null;

        var identity = ControlClaimsIdentityMapper.ToTrustedWorkspaceIdentity(result.Principal, options);
        var expiresAt = result.Properties?.ExpiresUtc;
        return identity is not null && expiresAt.HasValue
            ? new AuthenticatedControlSession(identity, expiresAt.Value)
            : null;
    }
}
