namespace ValenceControl.Api.Authentication;

public sealed class AdminDashboardAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/admin"))
            context.Request.Path = "/admin/";

        await next(context);
    }
}

public static class AdminDashboardAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseAdminDashboardAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<AdminDashboardAuthenticationMiddleware>();
}
