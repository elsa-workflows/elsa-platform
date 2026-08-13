using ValenceControl.Api.Authentication;
using ValenceControl.PackageCatalog.Core.Approvals;
using ValenceControl.PackageCatalog.Core.Manifests;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageManifests;
using System.Text.Json;

namespace ValenceControl.Api.Admin.Packages;

public static class AdminPackageEndpoints
{
    public static IEndpointRouteBuilder MapAdminPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/packages")
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Packages");

        group.MapGet("/", async (ApprovalService approvals, CancellationToken cancellationToken) =>
            Results.Ok((await approvals.ListPackagesAsync(cancellationToken)).Select(ToListResponse)));

        group.MapGet("/{packageId}", async (string packageId, ApprovalService approvals, CancellationToken cancellationToken) =>
        {
            var package = await approvals.GetPackageAsync(packageId, cancellationToken);
            return package is null ? Results.NotFound() : Results.Ok(ToResponse(package));
        });

        group.MapGet("/{packageId}/versions/{version}/manifest", async (string packageId, string version, IApprovalStore store, CancellationToken cancellationToken) =>
        {
            var packageVersion = await store.GetPackageVersionAsync(packageId, version, cancellationToken);
            if (packageVersion?.Package is null)
                return Results.NotFound();

            return Results.Ok(new AdminVersionManifestResponse(
                packageVersion.Package.PackageId,
                packageVersion.Version,
                !string.IsNullOrWhiteSpace(packageVersion.ManifestJson),
                packageVersion.SchemaVersion,
                packageVersion.ManifestHash,
                packageVersion.SuspiciousManifestHash,
                packageVersion.ManifestJson));
        });

        return endpoints;
    }

    internal static AdminPackageListResponse ToListResponse(Package package)
    {
        var latestVersion = package.Versions.FirstOrDefault(version => version.Version == package.LatestVersion) ?? package.Versions.FirstOrDefault();

        return new(
            package.PackageId,
            package.Approved,
            package.Listed,
            package.SourceId,
            package.LatestVersion,
            ToApprovalStatus(package, latestVersion),
            ToValidationStatus(latestVersion),
            latestVersion?.Features.Count ?? 0,
            package.CreatedAt,
            package.UpdatedAt,
            package.Versions.Select(ToListVersionResponse).ToList());
    }

    internal static AdminPackageResponse ToResponse(Package package)
    {
        var latestVersion = package.Versions.FirstOrDefault(version => version.Version == package.LatestVersion) ?? package.Versions.FirstOrDefault();

        return new(
            package.PackageId,
            package.Approved,
            package.Listed,
            package.SourceId,
            package.Source is null ? null : new AdminPackageSourceResponse(
                package.Source.Id,
                package.Source.Name,
                package.Source.Url,
                package.Source.Enabled,
                package.Source.Status,
                package.Source.LastSyncedAt,
                package.Source.LastSuccessfulSyncAt),
            package.LatestVersion,
            ToApprovalStatus(package, latestVersion),
            ToValidationStatus(latestVersion),
            latestVersion?.Features.Count ?? 0,
            package.CreatedAt,
            package.UpdatedAt,
            package.Versions.Select(version => ToVersionResponse(package, version)).ToList());
    }

    private static PackageApprovalStatus ToApprovalStatus(Package package, PackageVersion? latestVersion) =>
        latestVersion?.ApprovalStatus ?? (package.Approved ? PackageApprovalStatus.Approved : PackageApprovalStatus.Pending);

    private static ValidationStatus ToValidationStatus(PackageVersion? latestVersion) =>
        latestVersion?.ValidationStatus ?? ValidationStatus.NotValidated;

    private static AdminPackageListVersionResponse ToListVersionResponse(PackageVersion version) =>
        new(
            version.Version,
            version.ValidationStatus,
            version.ApprovalStatus,
            version.IsListed,
            version.SuspiciousChangeDetected,
            version.SchemaVersion,
            ApprovalService.CreateVersionStateToken(version));

    private static AdminPackageVersionResponse ToVersionResponse(Package package, PackageVersion version)
    {
        var manifest = ReadManifest(version.ManifestJson);
        var compatibility = manifest?.Compatibility;
        var compatibilityPackageRules = compatibility?.PackageRules ?? [];
        var runtimeCapabilities = compatibility?.RuntimeCapabilities ?? [];
        var requiredCapabilities = version.Features.SelectMany(x => DeserializeStringList(x.RequiredCapabilitiesJson)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var featureCategories = FeatureCategories(manifest);

        return new(
            version.Version,
            version.ValidationStatus,
            version.ApprovalStatus,
            version.IsListed,
            version.SuspiciousChangeDetected,
            version.SchemaVersion,
            version.ManifestHash,
            version.SuspiciousManifestHash,
            ApprovalService.CreateVersionStateToken(version),
            version.PublishedAt,
            version.IndexedAt,
            version.Features.Count,
            version.Features.Sum(x => x.Settings.Count),
            new AdminCompatibilityResponse(
                ReadTargetFrameworks(manifest),
                compatibility?.ElsaVersionRange,
                runtimeCapabilities.Concat(requiredCapabilities).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                compatibilityPackageRules.Select(x => x.Reason).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList(),
                compatibilityPackageRules.Select(x => $"{x.PackageId} {x.VersionRange}".Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()),
            VisibilityReasons(package, version),
            version.Features.Select(feature => ToFeatureResponse(feature, featureCategories)).ToList(),
            new AdminManifestResponse(
                !string.IsNullOrWhiteSpace(version.ManifestJson),
                version.SchemaVersion,
                version.ManifestHash,
                version.SuspiciousManifestHash,
                ""));
    }

    private static AdminFeatureResponse ToFeatureResponse(FeatureRecord feature, IReadOnlyDictionary<string, IReadOnlyList<string>> featureCategories) =>
        new(
            feature.FeatureId,
            feature.TypeName,
            feature.DisplayName,
            feature.Description,
            feature.Category,
            EffectiveCategories(feature, featureCategories),
            DeserializeStringList(feature.RequiredCapabilitiesJson),
            feature.DependenciesJson,
            feature.ConflictsJson,
            feature.InfrastructureJson,
            feature.Advanced,
            feature.Experimental,
            feature.ExtensionsJson,
            feature.Settings.Select(setting => new AdminFeatureSettingResponse(
                setting.Name,
                setting.ClrType,
                setting.JsonType,
                setting.Required,
                setting.DefaultValueJson,
                setting.DisplayName,
                setting.Description,
                setting.Category,
                setting.ValidationJson,
                setting.Secret,
                setting.RestartRequired,
                setting.EnvironmentVariable,
                setting.UiJson,
                setting.ExtensionsJson)).ToList());

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FeatureCategories(ElsaPackageManifest? manifest) =>
        manifest?.Features
            .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => EffectiveCategories(group.First()),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> EffectiveCategories(FeatureManifest feature)
    {
        var categories = (feature.Categories ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return categories.Length > 0
            ? categories
            : string.IsNullOrWhiteSpace(feature.Category) ? [] : [feature.Category.Trim()];
    }

    private static IReadOnlyList<string> EffectiveCategories(FeatureRecord feature, IReadOnlyDictionary<string, IReadOnlyList<string>> featureCategories) =>
        featureCategories.TryGetValue(feature.FeatureId, out var categories)
            ? categories
            : string.IsNullOrWhiteSpace(feature.Category) ? [] : [feature.Category.Trim()];

    private static IReadOnlyList<AdminVisibilityReasonResponse> VisibilityReasons(Package package, PackageVersion version)
    {
        var reasons = new List<AdminVisibilityReasonResponse>();
        if (!package.Approved)
            reasons.Add(Block("PackagePendingApproval", "TrustDecision", "This package is not approved."));
        if (!package.Listed)
            reasons.Add(Block("PackageUnlisted", "Listing", "This package is unlisted."));
        if (version.ApprovalStatus == PackageApprovalStatus.Pending)
            reasons.Add(Block("VersionPendingApproval", "TrustDecision", "This package version is pending approval."));
        if (version.ApprovalStatus == PackageApprovalStatus.Rejected)
            reasons.Add(Block("VersionRejected", "TrustDecision", "This package version is rejected."));
        if (version.ValidationStatus != ValidationStatus.Valid)
            reasons.Add(Block("ValidationNotValid", "Validation", $"Validation status is {version.ValidationStatus}."));
        if (!version.IsListed)
            reasons.Add(Block("VersionUnlisted", "Listing", "This package version is unlisted."));
        if (version.SuspiciousChangeDetected)
            reasons.Add(Block("SuspiciousManifestChange", "Manifest", "This immutable package version produced different manifest content."));
        if (string.IsNullOrWhiteSpace(version.ManifestJson))
            reasons.Add(Block("ManifestMissing", "Manifest", "Manifest content is missing."));

        return reasons.Count == 0
            ? [new("Visible", "TrustDecision", "Info", "This package version is approved, valid, and listed.", false)]
            : reasons;
    }

    private static AdminVisibilityReasonResponse Block(string code, string category, string message) =>
        new(code, category, "Blocking", message, true);

    private static ElsaPackageManifest? ReadManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ElsaPackageManifest>(json, ManifestJsonSerializerOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, ManifestJsonSerializerOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(ElsaPackageManifest? manifest)
    {
        if (manifest?.Extensions is not { } extensions || !extensions.TryGetValue("targetFrameworks", out var value) || value is null)
            return [];

        if (value is JsonElement element)
            return ReadTargetFrameworks(element);

        if (value is IEnumerable<string> frameworks)
            return frameworks.ToList();

        return [];
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();
    }
}
