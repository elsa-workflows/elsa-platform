using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Authentication;

public sealed class CustomerSessionIdentityReader(IOptions<PlatformIdentityOptions> options) : IWorkspaceIdentityReader
{
    private readonly PlatformIdentityOptions _options = options.Value;

    public async ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CustomerAuthenticationDefaults.CookieScheme);
        if (!result.Succeeded || result.Principal?.Identity is not { IsAuthenticated: true })
            return null;

        return PlatformClaimsIdentityMapper.ToTrustedWorkspaceIdentity(result.Principal, _options);
    }
}
