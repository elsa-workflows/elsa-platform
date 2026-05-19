using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.PackageCatalog.Api.Admin.Workspaces;

public static class AdminWorkspaceEntitlementEndpoints
{
    public static IEndpointRouteBuilder MapAdminWorkspaceEntitlementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/workspaces")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Workspaces");

        group.MapPut("/{workspaceId:guid}/entitlements", async (
            Guid workspaceId,
            WorkspaceEntitlementRequest request,
            IAccountWorkspaceStore store,
            CancellationToken cancellationToken) =>
        {
            if (request.MaxSources < 0)
                return Results.BadRequest(new WorkspaceValidationErrorResponse(["MaxSources must be greater than or equal to zero."]));

            if (!await store.WorkspaceExistsAsync(workspaceId, cancellationToken))
                return Results.NotFound();

            var entitlement = await store.SaveEntitlementAsync(new WorkspaceEntitlementSnapshot
            {
                WorkspaceId = workspaceId,
                CanCreateCustomSources = request.CanCreateCustomSources,
                MaxSources = request.MaxSources,
                MaxPackagesIndexed = request.MaxPackagesIndexed,
                MaxVersionsPerPackage = request.MaxVersionsPerPackage,
                MaxSyncsPerDay = request.MaxSyncsPerDay,
                PrivateFeedsEnabled = request.PrivateFeedsEnabled
            }, cancellationToken);

            return Results.Ok(ToResponse(entitlement));
        });

        return endpoints;
    }

    private static WorkspaceEntitlementResponse ToResponse(WorkspaceEntitlementSnapshot entitlement) =>
        new(
            entitlement.WorkspaceId,
            entitlement.CanCreateCustomSources,
            entitlement.MaxSources,
            entitlement.MaxPackagesIndexed,
            entitlement.MaxVersionsPerPackage,
            entitlement.MaxSyncsPerDay,
            entitlement.PrivateFeedsEnabled,
            entitlement.SyncedAt);
}
