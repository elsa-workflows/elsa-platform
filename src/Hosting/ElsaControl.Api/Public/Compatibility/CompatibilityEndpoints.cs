using ElsaControl.PackageCatalog.Abstractions.Compatibility;
using ElsaControl.PackageCatalog.Core.Compatibility;

namespace ElsaControl.Api.Public.Compatibility;

public static class CompatibilityEndpoints
{
    public static IEndpointRouteBuilder MapCompatibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/compatibility/check", async (CompatibilityCheckApiRequest request, CompatibilityCheckService compatibility, CancellationToken cancellationToken) =>
        {
            var result = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                request.ElsaVersion,
                request.DockerImageVersion,
                request.Packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                request.Features ?? []), cancellationToken);

            return Results.Ok(new CompatibilityCheckApiResponse(
                result.Compatible,
                result.Findings.Select(x => new CompatibilityFindingApiResponse(x.Severity, x.Code, x.Message)).ToList()));
        })
        .WithTags("Public Compatibility");

        return endpoints;
    }
}
