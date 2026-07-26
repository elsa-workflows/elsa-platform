using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Authentication;

public static class BuilderClientAuthorization
{
    public const string Policy = "BuilderClientApi";

    public static IServiceCollection AddBuilderClientAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(Policy, policy =>
            {
                policy.AuthenticationSchemes.Add(BuilderClientApiKeyAuthenticationDefaults.Scheme);
                policy.RequireAuthenticatedUser();
            }));
        return services;
    }
}

public static class BuilderClientApiKeyAuthenticationDefaults
{
    public const string Scheme = "BuilderClientApiKey";
    public const string ConfigurationKey = "Authentication:BuilderClientApiKey";
}

public sealed class BuilderClientApiKeyValidator(IConfiguration configuration)
{
    public bool IsValid(string? suppliedApiKey)
    {
        var configuredApiKey = configuration[BuilderClientApiKeyAuthenticationDefaults.ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey) || string.IsNullOrWhiteSpace(suppliedApiKey))
            return false;

        var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedApiKey);
        using var hmac = new HMACSHA256(configuredBytes);
        var configuredHash = hmac.ComputeHash(configuredBytes);
        var suppliedHash = hmac.ComputeHash(suppliedBytes);
        return CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
    }
}

public sealed class BuilderClientApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BuilderClientApiKeyValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var suppliedApiKey = values.FirstOrDefault();
        if (!validator.IsValid(suppliedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid builder client API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "builder-client"), new Claim(ClaimTypes.Name, "Runtime Builder Client")],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
