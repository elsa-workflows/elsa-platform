using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Authentication;

/// <summary>
/// Endpoint filters that resolve workspace access from the <c>workspaceId</c> route value and
/// expose the resolved <see cref="WorkspaceAccess"/> to handlers via <see cref="GetWorkspaceAccess"/>.
/// </summary>
public static class WorkspaceAccessEndpointFilters
{
    private const string AccessItemKey = "ElsaControl.WorkspaceAccess";

    public static RouteHandlerBuilder RequireWorkspaceAccess(
        this RouteHandlerBuilder builder,
        WorkspaceOperation operation = WorkspaceOperation.Read) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var denied = await ResolveWorkspaceAccessAsync(context.HttpContext, operation);
            return denied ?? await next(context);
        });

    /// <summary>
    /// Requires the caller to be a workspace <see cref="WorkspaceRole.Owner"/>. Non-owners get the
    /// result from <paramref name="onDenied"/>, defaulting to <see cref="Results.Forbid()"/>.
    /// </summary>
    public static RouteHandlerBuilder RequireWorkspaceOwner(this RouteHandlerBuilder builder, Func<IResult>? onDenied = null) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var denied = await ResolveWorkspaceAccessAsync(context.HttpContext, WorkspaceOperation.Read);
            if (denied is not null)
                return denied;

            return context.HttpContext.GetWorkspaceAccess().Role is WorkspaceRole.Owner
                ? await next(context)
                : onDenied?.Invoke() ?? Results.Forbid();
        });

    public static WorkspaceAccess GetWorkspaceAccess(this HttpContext context) =>
        context.Items[AccessItemKey] as WorkspaceAccess
        ?? throw new InvalidOperationException("Workspace access has not been resolved for this request.");

    internal static async Task<IResult?> ResolveWorkspaceAccessAsync(HttpContext context, WorkspaceOperation operation)
    {
        if (!Guid.TryParse(context.Request.RouteValues["workspaceId"]?.ToString(), out var workspaceId))
            return Results.NotFound();

        var resolver = context.RequestServices.GetRequiredService<WorkspaceAccessResolver>();
        var access = await resolver.ResolveAsync(context, workspaceId, operation, context.RequestAborted);
        if (!access.Succeeded)
            return access.ToHttpResult();

        context.Items[AccessItemKey] = access.Access;
        return null;
    }
}
