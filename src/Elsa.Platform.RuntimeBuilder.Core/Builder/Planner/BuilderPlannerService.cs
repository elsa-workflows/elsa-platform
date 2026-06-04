using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;

namespace Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;

public sealed class BuilderPlannerService(
    IPublicCatalogQueries catalog,
    IPackageCompatibilityService compatibility,
    InfrastructureProviderCatalog infrastructureProviders)
{
    private const string ServerRuntimeKind = "elsa.server";

    public async Task<BuilderPlanResult> PlanAsync(BuilderPlanRequest request, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        var packages = request.Intent.Packages.ToList();
        var infrastructure = request.Intent.Infrastructure.ToList();
        var autoPackages = new List<BundlePackageSelection>();
        var autoFeatures = new List<string>();
        var autoInfrastructure = new List<InfrastructureSelection>();

        var visiblePackages = workspaceId.HasValue
            ? await catalog.ListPackagesForWorkspaceAsync(workspaceId.Value, [], cancellationToken)
            : await catalog.ListPackagesAsync([], cancellationToken);
        visiblePackages = FilterServerPackages(visiblePackages);
        var packageLookup = visiblePackages
            .SelectMany(package => package.Versions.Select(version => new { Package = package, Version = version }))
            .ToList();

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var selected in packages.ToList())
            {
                var version = packageLookup.FirstOrDefault(x =>
                    x.Version.Source.Id == selected.SourceId &&
                    string.Equals(x.Version.PackageId, selected.PackageId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Version.Version, selected.Version, StringComparison.OrdinalIgnoreCase))?.Version;
                if (version is null)
                    continue;

                var selectedFeatures = selected.SelectedFeatures?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                foreach (var feature in version.Features.Where(x => selectedFeatures.Contains(x.FeatureId)))
                {
                    foreach (var dependency in feature.Dependencies.Where(x => !x.Optional))
                    {
                        if (!string.IsNullOrWhiteSpace(dependency.PackageId) && packages.All(x => !string.Equals(x.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase)))
                        {
                            var dependencyVersion = packageLookup
                                .Where(x => string.Equals(x.Version.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(x => x.Version.Version, StringComparer.OrdinalIgnoreCase)
                                .FirstOrDefault();
                            if (dependencyVersion is not null)
                            {
                                IReadOnlyList<string> dependencyFeatures = string.IsNullOrWhiteSpace(dependency.FeatureId) ? [] : [dependency.FeatureId];
                                var added = new BundlePackageSelection(dependencyVersion.Version.Source.Id, dependencyVersion.Version.PackageId, dependencyVersion.Version.Version, dependencyFeatures, null);
                                packages.Add(added);
                                autoPackages.Add(added);
                                if (!string.IsNullOrWhiteSpace(dependency.FeatureId))
                                    autoFeatures.Add(dependency.FeatureId);
                                changed = true;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(dependency.FeatureId) && !selectedFeatures.Contains(dependency.FeatureId))
                        {
                            selectedFeatures.Add(dependency.FeatureId);
                            ReplacePackageFeatures(packages, selected, selectedFeatures);
                            autoFeatures.Add(dependency.FeatureId);
                            changed = true;
                        }
                    }

                    foreach (var requirement in feature.Infrastructure.Where(x => !x.Optional))
                    {
                        if (infrastructure.Any(x => string.Equals(x.Kind, requirement.Kind, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var provider = infrastructureProviders.ListProviders()
                            .FirstOrDefault(x =>
                                string.Equals(x.Kind, requirement.Kind, StringComparison.OrdinalIgnoreCase) &&
                                (requirement.Providers.Count == 0 || requirement.Providers.Contains(x.Provider, StringComparer.OrdinalIgnoreCase)));
                        if (provider is null)
                            continue;

                        var added = new InfrastructureSelection(provider.Kind, provider.Id, provider.Strategy, null);
                        infrastructure.Add(added);
                        autoInfrastructure.Add(added);
                    }
                }
            }
        }

        var features = packages
            .SelectMany(x => x.SelectedFeatures ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var compatibilityResult = await compatibility.CheckAsync(new CompatibilityCheckRequest(
            null,
            request.Intent.Image.Tag,
            packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
            features,
            workspaceId), cancellationToken);

        var resolved = request.Intent with
        {
            Packages = packages
                .OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Infrastructure = infrastructure
                .DistinctBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        return new BuilderPlanResult(
            resolved,
            new BuilderPlanAutoAdded(autoPackages, autoFeatures.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(), autoInfrastructure),
            compatibilityResult.Findings.Select(x => new BundleFinding(x.Severity, x.Code, x.Message, null)).ToList());
    }

    private static void ReplacePackageFeatures(List<BundlePackageSelection> packages, BundlePackageSelection selected, HashSet<string> selectedFeatures)
    {
        var index = packages.FindIndex(x => x.SourceId == selected.SourceId && string.Equals(x.PackageId, selected.PackageId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Version, selected.Version, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            packages[index] = selected with { SelectedFeatures = selectedFeatures.Order(StringComparer.OrdinalIgnoreCase).ToList() };
    }

    private static IReadOnlyList<PublicPackageProjection> FilterServerPackages(IReadOnlyList<PublicPackageProjection> packages) =>
        packages
            .Select(package => package with
            {
                RuntimeKinds = package.RuntimeKinds.Where(IsServerRuntimeKind).ToList(),
                Versions = package.Versions
                    .Where(version => version.RuntimeKinds.Any(IsServerRuntimeKind))
                    .Select(version => version with
                    {
                        Features = version.Features
                            .Where(feature => feature.RuntimeKinds.Any(IsServerRuntimeKind))
                            .ToList()
                    })
                    .ToList()
            })
            .Where(package => package.Versions.Count > 0)
            .ToList();

    private static bool IsServerRuntimeKind(string runtimeKind) =>
        string.Equals(runtimeKind, ServerRuntimeKind, StringComparison.OrdinalIgnoreCase);
}
