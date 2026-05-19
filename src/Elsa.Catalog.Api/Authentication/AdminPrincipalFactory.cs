using System.Security.Claims;

namespace Elsa.Catalog.Api.Authentication;

public static class AdminPrincipalFactory
{
    public static ClaimsPrincipal Create(string name, string authenticationScheme)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name)],
            authenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
