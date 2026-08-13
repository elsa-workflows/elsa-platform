using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Authentication;

public sealed class CustomerSessionIdentityReader(IOptions<ControlIdentityOptions> options) : IWorkspaceIdentityReader
{
    private readonly ControlIdentityOptions _options = options.Value;

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!result.Succeeded || result.Principal?.Identity is not { IsAuthenticated: true })
            return null;

        return ControlClaimsIdentityMapper.ToTrustedWorkspaceIdentity(result.Principal, _options);
    }
}
