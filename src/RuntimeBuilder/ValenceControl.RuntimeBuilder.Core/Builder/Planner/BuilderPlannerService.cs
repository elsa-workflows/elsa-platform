using System.Diagnostics;
using ValenceControl.PackageCatalog.Abstractions.Catalog;
using ValenceControl.PackageCatalog.Abstractions.Compatibility;
using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Abstractions.Planner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ValenceControl.RuntimeBuilder.Core.Builder.Planner;

public sealed class BuilderPlannerService(
    IPublicCatalogQueries catalog,
    IPackageCompatibilityService compatibility,
    RuntimeImageCatalog runtimeImages,
    InfrastructureProviderCatalog infrastructureProviders,
    IOptions<RuntimeBuilderOptions> options,
    ILogger<BuilderPlannerService> logger)
{
    public async Task<BuilderPlanResult> PlanAsync(BuilderPlanRequest request, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.PlanTimeoutSeconds));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var planningCancellationToken = deadline.Token;
        var totalStarted = Stopwatch.GetTimestamp();
        var phase = BuilderPlanningPhases.CatalogLoad;
        double catalogLoadMs = 0;
        double sourceMetadataResolutionMs = 0;
        long featureDependencyGraphTicks = 0;
        long infrastructureMatchingTicks = 0;
        double compatibilityMs = 0;
        var dependencyIterations = 0;
        var packages = request.Intent.Packages.ToList();
        var infrastructure = request.Intent.Infrastructure.ToList();
        var autoPackages = new List<BundlePackageSelection>();
        var autoFeatures = new List<string>();
        var autoInfrastructure = new List<InfrastructureSelection>();
        var findings = new List<BundleFinding>();
        var runtimeKinds = runtimeImages.Find(request.Intent.Image.Slug)?.RuntimeKinds ?? [];

        try
        {
            var catalogLoadStarted = Stopwatch.GetTimestamp();
            var sourceIds = request.Intent.Packages
                .Select(package => package.SourceId)
                .Concat(request.Intent.PackageSources.Select(source => source.SourceId))
                .Where(sourceId => sourceId != Guid.Empty)
                .Distinct()
                .ToList();
            var visiblePackages = workspaceId.HasValue
                ? await catalog.ListPackagesForWorkspaceAsync(workspaceId.Value, sourceIds, planningCancellationToken)
                : await catalog.ListPackagesAsync(sourceIds, planningCancellationToken);
            catalogLoadMs = Stopwatch.GetElapsedTime(catalogLoadStarted).TotalMilliseconds;

            phase = BuilderPlanningPhases.SourceMetadataResolution;
            var sourceMetadataStarted = Stopwatch.GetTimestamp();
            visiblePackages = FilterPackagesByRuntimeKinds(visiblePackages, runtimeKinds);
            var packageLookup = visiblePackages
                .SelectMany(package => package.Versions.Select(version => new PackageVersionLookupItem(package, version)))
                .ToList();
            sourceMetadataResolutionMs = Stopwatch.GetElapsedTime(sourceMetadataStarted).TotalMilliseconds;

            var changed = true;
            while (changed)
            {
                planningCancellationToken.ThrowIfCancellationRequested();
                dependencyIterations++;
                changed = false;
                foreach (var selected in packages.ToList())
                {
                    planningCancellationToken.ThrowIfCancellationRequested();
                    var version = packageLookup.FirstOrDefault(x =>
                        x.Version.Source.Id == selected.SourceId &&
                        string.Equals(x.Version.PackageId, selected.PackageId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Version.Version, selected.Version, StringComparison.OrdinalIgnoreCase))?.Version;
                    if (version is null)
                        continue;

                    var selectedFeatures = selected.SelectedFeatures?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                    foreach (var feature in version.Features.Where(x => selectedFeatures.Contains(x.FeatureId)))
                    {
                        planningCancellationToken.ThrowIfCancellationRequested();
                        phase = BuilderPlanningPhases.FeatureDependencyGraph;
                        var dependencyStarted = Stopwatch.GetTimestamp();
                        foreach (var dependency in feature.Dependencies.Where(x => !x.Optional))
                        {
                            if (!string.IsNullOrWhiteSpace(dependency.PackageId)
                                && string.IsNullOrWhiteSpace(dependency.FeatureId)
                                && packages.All(x => !string.Equals(x.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase)))
                            {
                                var dependencyVersion = packageLookup
                                    .Where(x => string.Equals(x.Version.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(x => x.Version.Version, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault();
                                if (dependencyVersion is not null)
                                {
                                    var added = new BundlePackageSelection(dependencyVersion.Version.Source.Id, dependencyVersion.Version.PackageId, dependencyVersion.Version.Version, [], null);
                                    packages.Add(added);
                                    autoPackages.Add(added);
                                    changed = true;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(dependency.FeatureId) && !selectedFeatures.Contains(dependency.FeatureId))
                            {
                                var dependencyFeature = ResolveDependencyFeature(selected, version, dependency, packageLookup);
                                if (dependencyFeature is null)
                                    continue;

                                if (!RuntimeKindCompatibilityPolicy.IsCompatible(dependencyFeature.Feature.RuntimeKinds, runtimeKinds))
                                {
                                    findings.Add(BundleFinding.Error("feature.runtimeKindUnsupported", $"{dependency.FeatureId} is not compatible with the selected runtime image.", $"feature:{dependency.FeatureId}"));
                                    continue;
                                }

                                bool dependencyChanged;
                                if (string.Equals(dependencyFeature.Version.PackageId, selected.PackageId, StringComparison.OrdinalIgnoreCase)
                                    && dependencyFeature.Version.Source.Id == selected.SourceId
                                    && string.Equals(dependencyFeature.Version.Version, selected.Version, StringComparison.OrdinalIgnoreCase))
                                {
                                    dependencyChanged = selectedFeatures.Add(dependencyFeature.Feature.FeatureId);
                                    if (dependencyChanged)
                                        ReplacePackageFeatures(packages, selected, selectedFeatures);
                                }
                                else
                                {
                                    dependencyChanged = AddOrUpdatePackageFeature(packages, dependencyFeature.Version, dependencyFeature.Feature.FeatureId, autoPackages);
                                }

                                if (dependencyChanged)
                                {
                                    autoFeatures.Add(dependencyFeature.Feature.FeatureId);
                                    changed = true;
                                }
                            }
                        }
                        featureDependencyGraphTicks += Stopwatch.GetTimestamp() - dependencyStarted;

                        phase = BuilderPlanningPhases.InfrastructureMatching;
                        var infrastructureStarted = Stopwatch.GetTimestamp();
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
                        infrastructureMatchingTicks += Stopwatch.GetTimestamp() - infrastructureStarted;
                    }
                }
            }

            phase = BuilderPlanningPhases.Compatibility;
            var features = packages
                .SelectMany(x => x.SelectedFeatures ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var compatibilityStarted = Stopwatch.GetTimestamp();
            var compatibilityResult = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                null,
                request.Intent.Image.Tag,
                packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                features,
                workspaceId,
                runtimeKinds), planningCancellationToken);
            compatibilityMs = Stopwatch.GetElapsedTime(compatibilityStarted).TotalMilliseconds;
            findings.AddRange(compatibilityResult.Findings.Select(x => new BundleFinding(x.Severity, x.Code, x.Message, null)));

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
            logger.LogInformation(
                "Builder plan completed outcome=success packages={PackageCount} features={FeatureCount} infrastructure={InfrastructureCount} dependencyIterations={DependencyIterations} catalogLoadMs={CatalogLoadMs:F1} sourceMetadataResolutionMs={SourceMetadataResolutionMs:F1} featureDependencyGraphMs={FeatureDependencyGraphMs:F1} infrastructureMatchingMs={InfrastructureMatchingMs:F1} compatibilityMs={CompatibilityMs:F1} totalMs={TotalMs:F1}",
                packages.Count,
                features.Count,
                infrastructure.Count,
                dependencyIterations,
                catalogLoadMs,
                sourceMetadataResolutionMs,
                TicksToMilliseconds(featureDependencyGraphTicks),
                TicksToMilliseconds(infrastructureMatchingTicks),
                compatibilityMs,
                Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
            return new BuilderPlanResult(
                resolved,
                new BuilderPlanAutoAdded(autoPackages, autoFeatures.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(), autoInfrastructure),
                findings);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Builder plan timed out phase={Phase} timeoutSeconds={TimeoutSeconds} packages={PackageCount} dependencyIterations={DependencyIterations} catalogLoadMs={CatalogLoadMs:F1} sourceMetadataResolutionMs={SourceMetadataResolutionMs:F1} featureDependencyGraphMs={FeatureDependencyGraphMs:F1} infrastructureMatchingMs={InfrastructureMatchingMs:F1} compatibilityMs={CompatibilityMs:F1} totalMs={TotalMs:F1}",
                phase,
                timeout.TotalSeconds,
                packages.Count,
                dependencyIterations,
                catalogLoadMs,
                sourceMetadataResolutionMs,
                TicksToMilliseconds(featureDependencyGraphTicks),
                TicksToMilliseconds(infrastructureMatchingTicks),
                compatibilityMs,
                Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
            throw new BuilderPlanningTimeoutException(phase, timeout, exception);
        }
    }

    private static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static void ReplacePackageFeatures(List<BundlePackageSelection> packages, BundlePackageSelection selected, HashSet<string> selectedFeatures)
    {
        var index = packages.FindIndex(x => x.SourceId == selected.SourceId && string.Equals(x.PackageId, selected.PackageId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Version, selected.Version, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            packages[index] = selected with { SelectedFeatures = selectedFeatures.Order(StringComparer.OrdinalIgnoreCase).ToList() };
    }

    private static ResolvedDependencyFeature? ResolveDependencyFeature(
        BundlePackageSelection sourceSelection,
        PublicPackageVersionProjection sourceVersion,
        PublicDependencyProjection dependency,
        IReadOnlyList<PackageVersionLookupItem> packageLookup)
    {
        var requestedFeatureId = dependency.FeatureId;
        if (string.IsNullOrWhiteSpace(requestedFeatureId))
            return null;

        var candidateVersions = packageLookup
            .Where(x => string.IsNullOrWhiteSpace(dependency.PackageId) || string.Equals(x.Version.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Version.Source.Id == sourceSelection.SourceId && string.Equals(x.Version.PackageId, sourceSelection.PackageId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => string.Equals(x.Version.Version, sourceVersion.Version, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Version.Version, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Version)
            .ToList();

        foreach (var version in candidateVersions)
        {
            var feature = version.Features.FirstOrDefault(x => FeatureDependencyIdentityPolicy.Matches(x.FeatureId, ShellFeatureName(x.ExtensionsJson), requestedFeatureId));
            if (feature is not null)
                return new ResolvedDependencyFeature(version, feature);
        }

        return null;
    }

    private static bool AddOrUpdatePackageFeature(
        List<BundlePackageSelection> packages,
        PublicPackageVersionProjection version,
        string featureId,
        List<BundlePackageSelection> autoPackages)
    {
        var existing = packages.FirstOrDefault(x =>
            x.SourceId == version.Source.Id
            && string.Equals(x.PackageId, version.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, version.Version, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var selectedFeatures = existing.SelectedFeatures?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (!selectedFeatures.Add(featureId))
                return false;

            ReplacePackageFeatures(packages, existing, selectedFeatures);
            return true;
        }

        var added = new BundlePackageSelection(version.Source.Id, version.PackageId, version.Version, [featureId], null);
        packages.Add(added);
        autoPackages.Add(added);
        return true;
    }

    private static string? ShellFeatureName(string extensionsJson)
    {
        if (string.IsNullOrWhiteSpace(extensionsJson))
            return null;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(extensionsJson);
            return document.RootElement.TryGetProperty("cshellsFeatureName", out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record PackageVersionLookupItem(PublicPackageProjection Package, PublicPackageVersionProjection Version);

    private sealed record ResolvedDependencyFeature(PublicPackageVersionProjection Version, PublicFeatureProjection Feature);

    private static IReadOnlyList<PublicPackageProjection> FilterPackagesByRuntimeKinds(
        IReadOnlyList<PublicPackageProjection> packages,
        IReadOnlyList<string> runtimeKinds) =>
        packages
            .Select(package => package with
            {
                RuntimeKinds = package.RuntimeKinds
                    .Where(runtimeKind => RuntimeKindCompatibilityPolicy.IsCompatible([runtimeKind], runtimeKinds))
                    .ToList(),
                Versions = package.Versions
                    .Where(version => RuntimeKindCompatibilityPolicy.IsCompatible(version.RuntimeKinds, runtimeKinds))
                    .Select(version => version with
                    {
                        Features = version.Features
                            .Where(feature => RuntimeKindCompatibilityPolicy.IsCompatible(feature.RuntimeKinds, runtimeKinds))
                            .ToList()
                    })
                    .ToList()
            })
            .Where(package => package.Versions.Count > 0)
            .ToList();
}

public static class BuilderPlanningPhases
{
    public const string CatalogLoad = "catalogLoad";
    public const string SourceMetadataResolution = "sourceMetadataResolution";
    public const string FeatureDependencyGraph = "featureDependencyGraph";
    public const string InfrastructureMatching = "infrastructureMatching";
    public const string Compatibility = "compatibility";
}

public sealed class BuilderPlanningTimeoutException(string phase, TimeSpan timeout, Exception innerException)
    : TimeoutException($"Builder planning exceeded the {timeout.TotalSeconds:0}-second deadline during {phase}.", innerException)
{
    public string Phase { get; } = phase;
    public TimeSpan Timeout { get; } = timeout;
}
