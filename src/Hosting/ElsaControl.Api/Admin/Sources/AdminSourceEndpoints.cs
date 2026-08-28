using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sources;
using ElsaControl.PackageCatalog.Core.Sync;

namespace ElsaControl.Api.Admin.Sources;

public static class AdminSourceEndpoints
{
    public static IEndpointRouteBuilder MapAdminSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/sources")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Sources");

        group.MapGet("/", async (PackageSourceService sources, SourceSyncActivityTracker syncActivity, CancellationToken cancellationToken) =>
        {
            var sourceList = await sources.ListAsync(cancellationToken);
            var packageCounts = await sources.GetPackageCountsAsync(sourceList.Select(x => x.Id).ToList(), cancellationToken);
            var syncingSourceIds = syncActivity.GetSyncingSourceIds();
            return Results.Ok(sourceList.Select(source => ToResponse(source, packageCounts.GetValueOrDefault(source.Id), syncingSourceIds.Contains(source.Id))));
        });

        group.MapGet("/{id:guid}", async (Guid id, PackageSourceService sources, SourceSyncActivityTracker syncActivity, CancellationToken cancellationToken) =>
        {
            var source = await sources.GetAsync(id, cancellationToken);
            return source is null ? Results.NotFound() : Results.Ok(ToResponse(source, await sources.GetPackageCountAsync(source.Id, cancellationToken), syncActivity.IsSourceSyncing(source.Id)));
        });

        group.MapPost("/", async (AdminSourceRequest request, PackageSourceService sources, SourceSyncActivityTracker syncActivity, CancellationToken cancellationToken) =>
        {
            var result = await sources.CreateAsync(ToSource(request), cancellationToken);
            return await ToResultAsync(result, sources, syncActivity, cancellationToken);
        });

        group.MapPut("/{id:guid}", async (Guid id, AdminSourceRequest request, PackageSourceService sources, SourceSyncActivityTracker syncActivity, CancellationToken cancellationToken) =>
        {
            var result = await sources.UpdateAsync(id, ToSource(request), cancellationToken);
            return await ToResultAsync(result, sources, syncActivity, cancellationToken);
        });

        group.MapDelete("/{id:guid}", async (Guid id, PackageSourceService sources, CancellationToken cancellationToken) =>
            await sources.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        return endpoints;
    }

    private static async Task<IResult> ToResultAsync(PackageSourceResult result, PackageSourceService sources, SourceSyncActivityTracker syncActivity, CancellationToken cancellationToken)
    {
        if (result.NotFoundResult)
            return Results.NotFound();

        if (!result.Succeeded)
            return Results.BadRequest(new AdminValidationErrorResponse(result.Errors));

        return Results.Ok(ToResponse(result.Source!, await sources.GetPackageCountAsync(result.Source!.Id, cancellationToken), syncActivity.IsSourceSyncing(result.Source!.Id)));
    }

    private static PackageSource ToSource(AdminSourceRequest request) => new()
    {
        Name = request.Name,
        Type = PackageSourceType.NuGetFeed,
        Url = request.Url,
        Enabled = request.Enabled,
        IncludePatterns = request.IncludePatterns.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
        ExcludePatterns = request.ExcludePatterns?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? [],
        ApprovalPolicy = request.ApprovalPolicy,
        VersionDiscoveryPolicy = request.VersionDiscoveryPolicy,
        PollingInterval = string.IsNullOrWhiteSpace(request.PollingInterval) ? null : request.PollingInterval.Trim()
    };

    private static AdminSourceResponse ToResponse(PackageSource source, int packageCount, bool isSyncing) =>
        new(
            source.Id,
            source.Name,
            source.Type,
            source.Url,
            source.Enabled,
            source.IncludePatterns,
            source.ExcludePatterns,
            source.ApprovalPolicy,
            source.VersionDiscoveryPolicy,
            source.Status,
            isSyncing,
            source.LastSyncedAt,
            source.LastSuccessfulSyncAt,
            source.LastSyncError,
            packageCount,
            source.SoftDeletedAt,
            source.PollingInterval,
            source.CreatedAt,
            source.UpdatedAt);
}
