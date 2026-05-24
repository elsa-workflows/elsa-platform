using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Approvals;
using System.Text.Json;

namespace Elsa.Platform.Api.Admin.Packages;

public static class AdminValidationEndpoints
{
    public static IEndpointRouteBuilder MapAdminValidationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/packages")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Validation");

        group.MapGet("/{packageId}/versions/{version}/validation", async (string packageId, string version, IApprovalStore store, CancellationToken cancellationToken) =>
        {
            var packageVersion = await store.GetPackageVersionAsync(packageId, version, cancellationToken);
            if (packageVersion is null)
                return Results.NotFound();

            var results = await store.GetValidationResultsAsync(packageVersion, cancellationToken);
            return Results.Ok(new AdminValidationFindingsResponse(
                packageVersion.Package?.PackageId ?? packageId,
                version,
                results.SelectMany(x => ToFindings(x.ErrorsJson, "Error", true, x.ValidatedAt, x.ValidatorVersion)
                    .Concat(ToFindings(x.WarningsJson, "Warning", false, x.ValidatedAt, x.ValidatorVersion))).ToList()));
        });

        return endpoints;
    }

    private static IReadOnlyList<AdminValidationFindingResponse> ToFindings(string json, string severity, bool blocksPublicVisibility, DateTimeOffset validatedAt, string? validatorVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(element => ToFinding(element, severity, blocksPublicVisibility, validatedAt, validatorVersion)).ToList()
                : [ToFinding(document.RootElement, severity, blocksPublicVisibility, validatedAt, validatorVersion)];
        }
        catch (JsonException)
        {
            return [new(severity, null, "Validation result could not be parsed.", null, blocksPublicVisibility, validatedAt, validatorVersion)];
        }
    }

    private static AdminValidationFindingResponse ToFinding(JsonElement element, string severity, bool blocksPublicVisibility, DateTimeOffset validatedAt, string? validatorVersion)
    {
        if (element.ValueKind == JsonValueKind.String)
            return new(severity, null, element.GetString() ?? "", null, blocksPublicVisibility, validatedAt, validatorVersion);

        if (element.ValueKind != JsonValueKind.Object)
            return new(severity, null, element.ToString(), null, blocksPublicVisibility, validatedAt, validatorVersion);

        return new(
            severity,
            TryGetString(element, "code") ?? TryGetString(element, "ruleId"),
            TryGetString(element, "message") ?? element.ToString(),
            TryGetString(element, "path") ?? TryGetString(element, "fieldPath"),
            blocksPublicVisibility,
            validatedAt,
            validatorVersion);
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
