namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class AdminDashboardAuthenticationDefaults
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan LoginThrottleWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LoginThrottleDelay = TimeSpan.FromMinutes(5);
    public const int LoginThrottleFailureThreshold = 5;
    public const string Scheme = "AdminDashboardCookie";
    public const string CookieName = "__Host-ElsaCatalogAdmin";
    public const string LoginPath = "/admin/login";
    public const string LogoutPath = "/admin/logout";
    public const string DefaultReturnPath = "/admin/overview";
}
