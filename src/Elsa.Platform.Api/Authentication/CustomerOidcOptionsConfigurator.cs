using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Platform.Api.Authentication;

internal static class CustomerOidcOptionsConfigurator
{
    public static void Configure(OpenIdConnectOptions options, PlatformIdentityOptions platformIdentity)
    {
        options.SignInScheme = CustomerAuthenticationDefaults.CookieScheme;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = platformIdentity.RequireHttpsMetadata;
        options.Authority = string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority;
        options.ClientId = string.IsNullOrWhiteSpace(platformIdentity.ClientId) ? null : platformIdentity.ClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(platformIdentity.ClientSecret) ? null : platformIdentity.ClientSecret;
        options.CallbackPath = PathStringFromUri(platformIdentity.RedirectUri, CustomerAuthenticationDefaults.CallbackPath);
        options.CorrelationCookie.Path = options.CallbackPath;
        options.NonceCookie.Path = options.CallbackPath;
        options.SignedOutCallbackPath = PathStringFromUri(platformIdentity.PostLogoutRedirectUri, "/api/auth/logout-callback");
        options.SignedOutRedirectUri = string.IsNullOrWhiteSpace(platformIdentity.PostLogoutRedirectUri)
            ? CustomerAuthenticationDefaults.DefaultReturnPath
            : platformIdentity.PostLogoutRedirectUri;
        options.Scope.Clear();
        foreach (var scope in platformIdentity.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Select(scope => scope.Trim()).Distinct(StringComparer.Ordinal))
            options.Scope.Add(scope);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(platformIdentity.Issuer) || !string.IsNullOrWhiteSpace(platformIdentity.Authority),
            ValidIssuer = string.IsNullOrWhiteSpace(platformIdentity.Issuer)
                ? (string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority)
                : platformIdentity.Issuer,
            NameClaimType = platformIdentity.Claims.DisplayName.FirstOrDefault() ?? "name",
            RoleClaimType = "role",
            ValidateAudience = !string.IsNullOrWhiteSpace(platformIdentity.ClientId),
            ValidAudience = string.IsNullOrWhiteSpace(platformIdentity.ClientId) ? null : platformIdentity.ClientId
        };
        options.Events.OnTokenValidated = context =>
        {
            if (context.Properties is { } properties)
                properties.StoreTokens(properties.GetTokens().Where(token => token.Name == "id_token"));

            return Task.CompletedTask;
        };
    }

    private static PathString PathStringFromUri(string? uri, string fallback)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return new PathString(fallback);

        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
            return new PathString(absolute.AbsolutePath);

        return uri.StartsWith("/", StringComparison.Ordinal)
            ? new PathString(uri)
            : new PathString(fallback);
    }
}
