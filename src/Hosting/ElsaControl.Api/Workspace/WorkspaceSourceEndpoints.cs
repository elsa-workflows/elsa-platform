using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sources;

namespace ElsaControl.Api.Workspace;

public static class WorkspaceSourceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/sources")
            .WithTags("Workspace Sources");

        group.MapGet("/", async (
            Guid workspaceId,
            IAccountWorkspaceStore store,
            CancellationToken cancellationToken) =>
        {
            var sources = await store.ListVisibleSourcesAsync(workspaceId, cancellationToken);
            var counts = await store.GetPackageCountsAsync(sources.Select(x => x.Id).ToList(), cancellationToken);
            return Results.Ok(sources.Select(source => ToResponse(source, counts.GetValueOrDefault(source.Id))));
        }).RequireWorkspaceAccess();

        group.MapPost("/", async (
            WorkspaceSourceRequest request,
            HttpContext context,
            WorkspaceSourceService workspaceSources,
            PackageSourceService packageSources,
            CancellationToken cancellationToken) =>
        {
            var result = await workspaceSources.CreateSourceAsync(context.GetWorkspaceAccess(), ToCreateRequest(request), cancellationToken);
            if (result.ForbiddenResult)
                return Results.Problem(title: "Workspace source creation is not allowed.", detail: string.Join(" ", result.Errors), statusCode: StatusCodes.Status403Forbidden);

            if (!result.Succeeded)
                return Results.BadRequest(new WorkspaceValidationErrorResponse(result.Errors));

            var packageCount = await packageSources.GetPackageCountAsync(result.Source!.Id, cancellationToken);
            return Results.Ok(ToResponse(result.Source!, packageCount));
        }).RequireWorkspaceAccess(WorkspaceOperation.ManageSources);

        return endpoints;
    }

    public static WorkspaceSourceResponse ToResponse(PackageSource source, int packageCount) =>
        new(source.Id, source.Name, PublicSourceUrlSanitizer.Sanitize(source.Url), source.Visibility, packageCount);

    private static WorkspaceSourceCreateRequest ToCreateRequest(WorkspaceSourceRequest request) =>
        new(
            request.Name,
            request.Url,
            request.Enabled,
            request.IncludePatterns.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
            request.ExcludePatterns?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? [],
            request.VersionDiscoveryPolicy);

}
