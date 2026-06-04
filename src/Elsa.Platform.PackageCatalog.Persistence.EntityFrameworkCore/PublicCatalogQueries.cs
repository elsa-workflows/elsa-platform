using System.Text.Json;
using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Elsa.Platform.PackageManifests;
using Elsa.Platform.PackageManifests.Compatibility;
using Elsa.Platform.PackageManifests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

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
            .Where(x => (x.Source!.Visibility == PackageSourceVisibility.Public && x.Source.OwnerWorkspaceId == null) ||
                        (workspaceId.HasValue && x.Source.Visibility == PackageSourceVisibility.Workspace && x.Source.OwnerWorkspaceId == workspaceId.Value))
            .Where(x => x.Approved && x.Listed)
            .Where(x => x.Versions.Any(version =>
                version.IsListed &&
                version.ApprovalStatus == PackageApprovalStatus.Approved &&
                version.ValidationStatus == ValidationStatus.Valid &&
                !version.SuspiciousChangeDetected));

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
        return new(
            version.Package?.PackageId ?? "",
            version.Version,
            ToSourceProjection(version.Package),
            version.SchemaVersion,
            compatibility.PackageRuntimeKinds,
            version.PublishedAt,
            version.Features.Select(feature => ToFeatureProjection(feature, version, compatibility)).ToList());
    }

    private static PublicFeatureProjection ToFeatureProjection(Core.Manifests.FeatureRecord feature, PackageVersion version, RuntimeCompatibility compatibility) =>
        new(
            feature.FeatureId,
            version.Package?.PackageId ?? "",
            version.Version,
            ToSourceProjection(version.Package),
            feature.TypeName,
            feature.DisplayName,
            feature.Description,
            feature.Category,
            RuntimeKindsForFeature(feature.FeatureId, compatibility),
            DeserializeList<string>(feature.RequiredCapabilitiesJson),
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

        var packageRuntimeKinds = RuntimeKindCompatibilityPolicy.ResolvePackageRuntimeKinds(manifest);
        var featureRuntimeKinds = (manifest?.Features ?? [])
            .ToDictionary(
                feature => feature.Id,
                feature => RuntimeKindCompatibilityPolicy.ResolveFeatureRuntimeKinds(feature, packageRuntimeKinds),
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
