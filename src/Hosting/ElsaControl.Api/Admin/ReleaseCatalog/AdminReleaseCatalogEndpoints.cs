using ElsaControl.Api.Authentication;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Admin.ReleaseCatalog;

public static class AdminReleaseCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminReleaseCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/release-catalog")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Release Catalog");

        group.MapPost("/manifests", async (
            AdminReleaseManifestIngestionRequest? request,
            IReleaseCatalogIngestionService ingestion,
            IOptions<ReleaseCatalogAdmissionOptions> configuredOptions,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
                return Problem(
                    httpContext,
                    "Invalid release-manifest request.",
                    "releaseCatalog.request.invalid",
                    StatusCodes.Status400BadRequest,
                    "A release-manifest request body is required.");

            var options = configuredOptions.Value;
            var result = await ingestion.AdmitAsync(
                new ReleaseManifestArtifact(
                    request.Reference ?? "",
                    request.Digest ?? "",
                    request.Payload ?? ""),
                options.ToAdmissionOptions(),
                cancellationToken);

            if (!result.Accepted)
            {
                var status = result.WriteStatus == GovernedReleaseCatalogWriteStatus.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
                var code = result.WriteStatus == GovernedReleaseCatalogWriteStatus.Conflict
                    ? "releaseCatalog.identity.conflict"
                    : "releaseCatalog.admission.rejected";
                var title = result.WriteStatus == GovernedReleaseCatalogWriteStatus.Conflict
                    ? "Release catalog identity conflict."
                    : "Release manifest was not admitted.";
                return Problem(
                    httpContext,
                    title,
                    code,
                    status,
                    "The release manifest could not be admitted into the governed catalog.",
                    result.Findings);
            }

            var response = new AdminReleaseCatalogAdmissionResponse(
                result.WriteStatus ?? GovernedReleaseCatalogWriteStatus.Stored,
                result.Entries.Select(ReleaseCatalogApiMappings.ToResponse).ToArray());

            return result.WriteStatus == GovernedReleaseCatalogWriteStatus.Stored
                ? Results.Created("/api/admin/release-catalog/manifests", response)
                : Results.Ok(response);
        }).WithMetadata(new RequestSizeLimitAttribute(4 * 1024 * 1024));

        return endpoints;
    }

    private static IResult Problem(
        HttpContext httpContext,
        string title,
        string code,
        int statusCode,
        string detail,
        IReadOnlyList<GovernedReleaseCatalogFinding>? findings = null) =>
        Results.Problem(
            type: $"urn:elsa-control:problem:{code.Replace(".", "-", StringComparison.Ordinal)}",
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = httpContext.TraceIdentifier,
                ["findings"] = findings?.Select(x => new { x.Code, x.Scope, x.Message }).ToArray()
            });
}
