namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminDashboardAuthenticationDefaults
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    public const string Scheme = "AdminDashboardCookie";
    public const string CookieName = "__Host-ElsaCatalogAdmin";
    public const string LoginPath = "/admin/login";
    public const string LogoutPath = "/admin/logout";
    public const string DefaultReturnPath = "/admin/overview";
}
