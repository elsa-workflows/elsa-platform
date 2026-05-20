using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AdminApiKeyValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var suppliedApiKey = values.FirstOrDefault();
        if (!validator.IsValid(suppliedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var principal = AdminPrincipalFactory.Create("api-key", Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
