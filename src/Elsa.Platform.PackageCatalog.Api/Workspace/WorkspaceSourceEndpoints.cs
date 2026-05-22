using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;

namespace Elsa.Platform.PackageCatalog.Api.Workspace;

public static class WorkspaceSourceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/sources")
            .WithTags("Workspace Sources");

        group.MapGet("/", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            IAccountWorkspaceStore store,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var sources = await store.ListVisibleSourcesAsync(workspaceId, cancellationToken);
            var counts = await store.GetPackageCountsAsync(sources.Select(x => x.Id).ToList(), cancellationToken);
            return Results.Ok(sources.Select(source => ToResponse(source, counts.GetValueOrDefault(source.Id))));
        });

        group.MapPost("/", async (
            Guid workspaceId,
            WorkspaceSourceRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspaceSourceService workspaceSources,
            PackageSourceService packageSources,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.ManageSources, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var result = await workspaceSources.CreateSourceAsync(access.Access!, ToCreateRequest(request), cancellationToken);
            if (result.ForbiddenResult)
                return Results.Problem(title: "Workspace source creation is not allowed.", detail: string.Join(" ", result.Errors), statusCode: StatusCodes.Status403Forbidden);

            if (!result.Succeeded)
                return Results.BadRequest(new WorkspaceValidationErrorResponse(result.Errors));

            var packageCount = await packageSources.GetPackageCountAsync(result.Source!.Id, cancellationToken);
            return Results.Ok(ToResponse(result.Source!, packageCount));
        });

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
