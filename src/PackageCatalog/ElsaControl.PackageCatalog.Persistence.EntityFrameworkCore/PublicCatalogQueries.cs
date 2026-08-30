using System.Text.Json;
using ElsaControl.PackageCatalog.Abstractions.Catalog;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Core.Compatibility;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sources;
using Elsa.Specifications.PackageManifests;
using Elsa.Specifications.PackageManifests.Compatibility;
using Elsa.Specifications.PackageManifests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class PublicCatalogQueries(CatalogDbContext dbContext) : IPublicCatalogQueries
{
    public async Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default)
    {
        var packages = await VisiblePackages(sourceIds)
            .OrderBy(x => x.PackageId)
            .ToListAsync(cancellationToken);

        return packages.Select(ToPackageProjection).ToList();
    }

    public async Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default)
    {
        var packages = await VisiblePackages(sourceIds, workspaceId)
            .OrderBy(x => x.PackageId)
            .ToListAsync(cancellationToken);

        return packages.Select(ToPackageProjection).ToList();
    }

    public async Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId])
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        return package is null ? null : ToPackageProjection(package);
    }

    public async Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId], workspaceId)
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        return package is null ? null : ToPackageProjection(package);
    }

    public async Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId])
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        return package?.Versions.Select(ToVersionProjection).ToList() ?? [];
    }

    public async Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId], workspaceId)
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        return package?.Versions.Select(ToVersionProjection).ToList() ?? [];
    }

    public async Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId])
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        var packageVersion = package?.Versions.SingleOrDefault(x => x.Version == version);
        return packageVersion is null ? null : ToVersionProjection(packageVersion);
    }

    public async Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default)
    {
        var package = await VisiblePackages([sourceId], workspaceId)
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

        var packageVersion = package?.Versions.SingleOrDefault(x => x.Version == version);
        return packageVersion is null ? null : ToVersionProjection(packageVersion);
    }

    public async Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default)
    {
        var packages = await VisiblePackages().ToListAsync(cancellationToken);
        return packages
            .SelectMany(x => x.Versions)
            .SelectMany(x => ToVersionProjection(x).Features)
            .OrderBy(x => x.FeatureId)
            .ToList();
    }

    public async Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default)
    {
        var packages = await VisiblePackages().ToListAsync(cancellationToken);
        return packages
            .SelectMany(x => x.Versions)
            .SelectMany(x => ToVersionProjection(x).Features)
            .FirstOrDefault(x => x.FeatureId == featureId);
    }

    private IQueryable<Package> VisiblePackages(IReadOnlyCollection<Guid>? sourceIds = null, Guid? workspaceId = null)
    {
        var query = dbContext.Packages
            .AsNoTracking()
            .Include(x => x.Source)
            .Include(x => x.Versions.Where(version =>
                version.IsListed &&
                version.ApprovalStatus == PackageApprovalStatus.Approved &&
                version.ValidationStatus == ValidationStatus.Valid &&
                !version.SuspiciousChangeDetected))
                .ThenInclude(x => x.Features)
                .ThenInclude(x => x.Settings)
            .Where(x => x.Source != null && x.Source.Enabled && x.Source.Browseable && x.Source.SoftDeletedAt == null)
            .Where(x => x.Approved && x.Listed)
            .Where(x => x.Versions.Any(version =>
                version.IsListed &&
                version.ApprovalStatus == PackageApprovalStatus.Approved &&
                version.ValidationStatus == ValidationStatus.Valid &&
                !version.SuspiciousChangeDetected));

        if (workspaceId is { } requestedWorkspaceId)
        {
            query = query.Where(x =>
                (x.Source!.Visibility == PackageSourceVisibility.Public && x.Source.OwnerWorkspaceId == null) ||
                (x.Source.Visibility == PackageSourceVisibility.Workspace &&
                 x.Source.OwnerWorkspaceId == requestedWorkspaceId &&
                 x.Source.OwnerWorkspace != null &&
                 x.Source.OwnerWorkspace.SoftDeletedAt == null));
        }
        else
        {
            query = query.Where(x => x.Source!.Visibility == PackageSourceVisibility.Public && x.Source.OwnerWorkspaceId == null);
        }

        return sourceIds is { Count: > 0 }
            ? query.Where(x => sourceIds.Contains(x.SourceId))
            : query;
    }

    private static bool IsLoadedVisibleVersion(PackageVersion version) =>
        version.IsListed &&
        version.ApprovalStatus == PackageApprovalStatus.Approved &&
        version.ValidationStatus == ValidationStatus.Valid &&
        !version.SuspiciousChangeDetected;

    private static PublicPackageProjection ToPackageProjection(Package package)
    {
        var versions = package.Versions.Where(IsLoadedVisibleVersion).ToList();
        var versionProjections = versions.Select(ToVersionProjection).ToList();
        var runtimeKinds = versionProjections
            .SelectMany(x => x.RuntimeKinds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new(
            package.PackageId,
            string.IsNullOrWhiteSpace(package.DisplayName) ? PackageDisplayNamePolicy.DefaultForPackageId(package.PackageId) : package.DisplayName,
            ToSourceProjection(package),
            runtimeKinds,
            package.LatestVersion,
            versionProjections);
    }

    private static PublicPackageVersionProjection ToVersionProjection(PackageVersion version)
    {
        var compatibility = ResolveRuntimeCompatibility(version);
        var featureCategories = GetFeatureCategories(version.ManifestJson);
        var versionRuntimeKinds = compatibility.PackageRuntimeKinds.Count > 0
            ? compatibility.PackageRuntimeKinds
            : compatibility.FeatureRuntimeKinds.Values
                .SelectMany(x => x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        return new(
            version.Package?.PackageId ?? "",
            version.Version,
            ToSourceProjection(version.Package),
            version.SchemaVersion,
            versionRuntimeKinds,
            version.PublishedAt,
            version.Features.Select(feature => ToFeatureProjection(feature, version, compatibility, featureCategories)).ToList(),
            NormalizeManifestDigest(version.ManifestHash));
    }

    private static string? NormalizeManifestDigest(string? manifestHash)
    {
        if (string.IsNullOrWhiteSpace(manifestHash))
            return null;

        var normalized = manifestHash.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["sha256:".Length..];

        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? $"sha256:{normalized.ToLowerInvariant()}"
            : null;
    }

    private static PublicFeatureProjection ToFeatureProjection(
        Core.Manifests.FeatureRecord feature,
        PackageVersion version,
        RuntimeCompatibility compatibility,
        IReadOnlyDictionary<string, IReadOnlyList<string>> featureCategories)
    {
        var categories = featureCategories.TryGetValue(feature.FeatureId, out var values)
            ? values
            : string.IsNullOrWhiteSpace(feature.Category) ? [] : [feature.Category];

        return
        new(
            feature.FeatureId,
            version.Package?.PackageId ?? "",
            version.Version,
            ToSourceProjection(version.Package),
            feature.TypeName,
            feature.DisplayName,
            feature.Description,
            categories.FirstOrDefault(),
            categories,
            DeserializeList<string>(feature.RequiredCapabilitiesJson),
            RuntimeKindsForFeature(feature.FeatureId, compatibility),
            DeserializeList<DependencyManifest>(feature.DependenciesJson)
                .Select(x => new PublicDependencyProjection(x.PackageId, x.VersionRange, x.FeatureId, x.Optional, x.Reason))
                .ToList(),
            DeserializeList<ConflictManifest>(feature.ConflictsJson)
                .Select(x => new PublicConflictProjection(x.PackageId, x.VersionRange, x.FeatureId, x.Reason))
                .ToList(),
            DeserializeList<InfrastructureRequirementManifest>(feature.InfrastructureJson)
                .Select(x => new PublicInfrastructureRequirementProjection(
                    x.Id,
                    x.Kind,
                    x.Optional,
                    x.Reason,
                    x.Capabilities,
                    x.Providers,
                    x.ConfigurationKeys,
                    JsonSerializer.Serialize(x.Extensions, ManifestJsonSerializerOptions.Default)))
                .ToList(),
            feature.Advanced,
            feature.Experimental,
            feature.ExtensionsJson,
            feature.Settings
                .Select(setting => new PublicFeatureSettingProjection(
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
                    setting.ExtensionsJson))
                .ToList());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> GetFeatureCategories(string manifestJson)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(manifestJson, ManifestJsonSerializerOptions.Default);
            return manifest?.Features.ToDictionary(
                feature => feature.Id,
                EffectiveCategories,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

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

    private static RuntimeCompatibility ResolveRuntimeCompatibility(PackageVersion version)
    {
        ElsaPackageManifest? manifest = null;
        try
        {
            manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(version.ManifestJson, ManifestJsonSerializerOptions.Default);
        }
        catch (JsonException)
        {
            // Invalid manifests are filtered out before projection. Fall back defensively for older data.
        }

        var packageRuntimeKinds = ElsaControl.PackageCatalog.Core.Compatibility.RuntimeKindCompatibilityPolicy.ResolvePackageRuntimeKinds(manifest);
        var featureRuntimeKinds = (manifest?.Features ?? [])
            .ToDictionary(
                feature => feature.Id,
                feature => ElsaControl.PackageCatalog.Core.Compatibility.RuntimeKindCompatibilityPolicy.ResolveFeatureRuntimeKinds(feature, packageRuntimeKinds),
                StringComparer.OrdinalIgnoreCase);

        return new RuntimeCompatibility(packageRuntimeKinds, featureRuntimeKinds);
    }

    private static IReadOnlyList<string> RuntimeKindsForFeature(string featureId, RuntimeCompatibility compatibility) =>
        compatibility.FeatureRuntimeKinds.TryGetValue(featureId, out var runtimeKinds)
            ? runtimeKinds
            : compatibility.PackageRuntimeKinds;

    private sealed record RuntimeCompatibility(
        IReadOnlyList<string> PackageRuntimeKinds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> FeatureRuntimeKinds);

    private static PublicPackageSourceProjection ToSourceProjection(Package? package)
    {
        var source = package?.Source ?? throw new InvalidOperationException("Visible package source was not loaded.");
        return new PublicPackageSourceProjection(source.Id, source.Name, PublicSourceUrlSanitizer.Sanitize(source.Url));
    }

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<T>>(json, ManifestJsonSerializerOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
