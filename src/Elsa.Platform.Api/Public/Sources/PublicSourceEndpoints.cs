using Elsa.Platform.PackageCatalog.Core.Sources;

namespace Elsa.Platform.Api.Public.Sources;

public static class PublicSourceEndpoints
{
    public static IEndpointRouteBuilder MapPublicSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sources").WithTags("Public Sources");

        group.MapGet("/", async (PublicSourceQueryService sources, CancellationToken cancellationToken) =>
        {
            var sourceList = await sources.ListSourcesAsync(cancellationToken);
            return Results.Ok(sourceList.Select(ToResponse));
        });

        return endpoints;
    }

    private static PublicSourceResponse ToResponse(PublicSourceProjection source) =>
        new(source.Id, source.Name, source.Url, source.PackageCount);
}
