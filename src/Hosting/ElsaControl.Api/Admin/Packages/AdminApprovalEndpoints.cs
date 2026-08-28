using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Approvals;
using ElsaControl.PackageCatalog.Core.Packages;
using System.Security.Claims;

namespace ElsaControl.Api.Admin.Packages;

public static class AdminApprovalEndpoints
{
    public static IEndpointRouteBuilder MapAdminApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/packages")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Approval");

        group.MapPost("/{packageId}/approve", async (string packageId, ApprovalRequest? request, HttpContext httpContext, ApprovalService approvals, CancellationToken cancellationToken) =>
            await approvals.SetPackageApprovalAsync(packageId, PackageApprovalStatus.Approved, GetActor(httpContext), request?.Reason, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapPost("/{packageId}/reject", async (string packageId, ApprovalRequest? request, HttpContext httpContext, ApprovalService approvals, CancellationToken cancellationToken) =>
            await approvals.SetPackageApprovalAsync(packageId, PackageApprovalStatus.Rejected, GetActor(httpContext), request?.Reason, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapPost("/{packageId}/versions/{version}/approve", async (string packageId, string version, ApprovalRequest? request, HttpContext httpContext, ApprovalService approvals, CancellationToken cancellationToken) =>
            ToResult(await approvals.TrySetVersionApprovalAsync(packageId, version, PackageApprovalStatus.Approved, GetActor(httpContext), request?.Reason, request?.ExpectedStateToken, cancellationToken)));

        group.MapPost("/{packageId}/versions/{version}/reject", async (string packageId, string version, ApprovalRequest? request, HttpContext httpContext, ApprovalService approvals, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Reason))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Rejection reason is required."] });

            return ToResult(await approvals.TrySetVersionApprovalAsync(packageId, version, PackageApprovalStatus.Rejected, GetActor(httpContext), request.Reason, request.ExpectedStateToken, cancellationToken));
        });

        return endpoints;
    }

    private static string GetActor(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.Identity?.Name
        ?? "unknown";

    private static IResult ToResult(VersionApprovalUpdateResult result) =>
        result switch
        {
            VersionApprovalUpdateResult.Updated => Results.NoContent(),
            VersionApprovalUpdateResult.Conflict => Results.Conflict(new { title = "Version state changed", detail = "Refresh package details before retrying this action." }),
            VersionApprovalUpdateResult.MissingStateToken => Results.ValidationProblem(new Dictionary<string, string[]> { ["expectedStateToken"] = ["Version state token is required."] }),
            _ => Results.NotFound()
        };
}
