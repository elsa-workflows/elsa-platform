using ElsaControl.Api.Authentication;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Workspace;

public static class ManagedElsaInstanceEndpoints
{
    public static IEndpointRouteBuilder MapManagedElsaInstanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/managed-elsa/instances", async (
            Guid workspaceId,
            HttpContext context,
            WorkspacePermissionService permissions,
            IManagedElsaInstanceCatalog instances,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            var canOpen = (await permissions.GetEffectivePermissionsAsync(
                    workspaceId,
                    context.GetWorkspaceAccess().AccountId,
                    cancellationToken))
                .Has(ManagedElsaInstancePermissions.Open);
            var summaries = await instances.ListAsync(workspaceId, cancellationToken);

            return Results.Ok(summaries.Select(summary => ToResponse(summary, canOpen)).ToList());
        })
        .WithTags("Managed Elsa Instances")
        .RequireWorkspaceAccess();

        return endpoints;
    }

    private static ManagedElsaInstanceResponse ToResponse(
        ManagedElsaInstanceSummary summary,
        bool canOpen)
    {
        var healthy = summary.DesiredLifecycle == ElsaDesiredLifecycle.Running &&
                      summary.ObservedLifecycle == ElsaObservedLifecycle.Ready &&
                      summary.Health == ElsaInstanceHealth.Healthy;
        var openable = canOpen && healthy && summary.Audience is not null && summary.CallbackUri is not null;
        return new ManagedElsaInstanceResponse(
            summary.OrganizationId,
            summary.InstanceId,
            summary.Name,
            summary.Slug,
            summary.DesiredLifecycle,
            summary.ObservedLifecycle,
            summary.Health,
            openable,
            openable ? summary.Audience : null,
            openable ? summary.CallbackUri!.OriginalString : null,
            !canOpen ? "Not authorized to open this instance." :
            !healthy ? "This instance is not currently available." :
            !openable ? "The current instance binding is unavailable." : null);
    }
}

public sealed record ManagedElsaInstanceResponse(
    Guid OrganizationId,
    Guid InstanceId,
    string Name,
    string Slug,
    ElsaDesiredLifecycle DesiredLifecycle,
    ElsaObservedLifecycle ObservedLifecycle,
    ElsaInstanceHealth Health,
    bool CanOpen,
    string? Audience,
    string? RedirectUri,
    string? UnavailableReason);
