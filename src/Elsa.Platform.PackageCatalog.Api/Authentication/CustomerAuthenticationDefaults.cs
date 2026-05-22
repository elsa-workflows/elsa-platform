namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class CustomerAuthenticationDefaults
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    public const string CookieScheme = "CustomerSession";
    public const string OidcScheme = "CustomerOidc";
    public const string CookieName = "__Host-ElsaPlatformCustomer";
    public const string LoginPath = "/api/auth/login";
    public const string LogoutPath = "/api/auth/logout";
    public const string SessionPath = "/api/auth/session";
    public const string CallbackPath = "/api/auth/callback";
    public const string DefaultReturnPath = "/admin/runtime-builder";
}
