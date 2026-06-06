using System.Diagnostics;
using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers;
using Elsa.Platform.RuntimeBuilder.DeploymentTemplates;
using Microsoft.Extensions.Logging;

namespace Elsa.Platform.RuntimeBuilder.Core.Builder;

public sealed class BundleGenerationService(
    IPublicCatalogQueries catalog,
    IPackageCompatibilityService compatibility,
    RuntimeImageCatalog runtimeImages,
    InfrastructureProviderCatalog infrastructureProviders,
    BuilderPlannerService planner,
    DeploymentTemplateRegistry deploymentTemplates,
    IEnumerable<IBundleFileRenderer> renderers,
    BundleFindingPolicy findingPolicy,
    BundleFilePolicy filePolicy,
    ILogger<BundleGenerationService> logger)
{
    private const string ServerRuntimeKind = "elsa.server";

    public async Task<BundleGenerationResult> GenerateAsync(RuntimeBuilderIntent intent, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = await planner.PlanAsync(new BuilderPlanRequest(intent), workspaceId, cancellationToken);
        intent = plan.Resolved;
        var target = deploymentTemplates.NormalizeTarget(intent.Target);
        var findings = new List<BundleFinding>();
        var resolved = await TryResolveAsync(intent, workspaceId, findings, cancellationToken);

        IReadOnlyList<BundleFile> files = [];
        if (resolved is not null && !findingPolicy.HasBlockingErrors(findings))
        {
            files = renderers
                .OrderBy(x => x.Order)
                .Select(renderer => renderer.Render(resolved, findings))
                .Concat(deploymentTemplates.Render(target, resolved, findings))
                .OrderBy(x => RequiredFileSortIndex(x.Path))
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .ToList();

            findings.AddRange(filePolicy.Validate(files, target));
            if (findingPolicy.HasBlockingErrors(findings))
                files = [];
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Generated builder bundle outcome={Outcome} image={ImageSlug} packages={PackageCount} features={FeatureCount} infrastructure={InfrastructureCount} files={FileCount} findings={FindingCount} durationMs={DurationMs}",
            findingPolicy.HasBlockingErrors(findings) ? "blocked" : findings.Any(x => x.Level == "warning") ? "warning" : "success",
            intent.Image.Slug,
            intent.Packages.Count,
            intent.Packages.Sum(x => x.SelectedFeatures?.Count ?? 0),
            intent.Infrastructure.Count,
            files.Count,
            findings.Count,
            stopwatch.ElapsedMilliseconds);

        return new BundleGenerationResult("preview", files, findings);
    }

    private async Task<BundleGenerationContext?> TryResolveAsync(RuntimeBuilderIntent intent, Guid? workspaceId, List<BundleFinding> findings, CancellationToken cancellationToken)
    {
        var runtimeImage = ValidateRuntimeImage(intent.Image, findings);
        ValidateLocalPackages(intent.LocalPackages, findings);
        var infrastructure = ValidateInfrastructure(intent.Infrastructure, findings);
        var packages = await ResolvePackagesAsync(intent.Packages, workspaceId, findings, cancellationToken);
        var sources = await ResolveSourcesAsync(intent.PackageSources, packages, workspaceId, findings, cancellationToken);

        if (runtimeImage is not null && packages.Count > 0 && !findingPolicy.HasBlockingErrors(findings))
        {
            var selectedFeatures = packages
                .SelectMany(x => x.SelectedFeatures)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var compatibilityResult = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                null,
                intent.Image.Tag ?? runtimeImage.DefaultTag,
                packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                selectedFeatures,
                workspaceId,
                runtimeImage.RuntimeKinds), cancellationToken);
            findings.AddRange(compatibilityResult.Findings.Select(ToBundleFinding));
        }

        if (runtimeImage is null || findingPolicy.HasBlockingErrors(findings))
            return null;

        return new BundleGenerationContext(
            intent,
            runtimeImage,
            NormalizeTag(intent.Image.Tag, runtimeImage),
            intent.Image.HostPort ?? runtimeImage.HostPort,
            packages,
            sources,
            infrastructure);
    }

    private RuntimeImage? ValidateRuntimeImage(RuntimeImageSelection selection, List<BundleFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(selection.Slug))
        {
            findings.Add(BundleFinding.Error("image.required", "Runtime image slug is required.", "image"));
            return null;
        }

        var image = runtimeImages.Find(selection.Slug);
        if (image is null)
        {
            findings.Add(BundleFinding.Error("runtimeImage.unknown", $"Runtime image {selection.Slug} is not supported.", $"image:{selection.Slug}"));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selection.Tag) && !image.AvailableTags.Contains(selection.Tag, StringComparer.OrdinalIgnoreCase))
            findings.Add(BundleFinding.Warning("image.tagUnverified", $"Runtime image tag {selection.Tag} is not in the curated tag list.", $"image:{selection.Slug}"));

        if (selection.HostPort is <= 0 or > 65535)
            findings.Add(BundleFinding.Error("image.invalidHostPort", "Host port must be between 1 and 65535.", $"image:{selection.Slug}"));

        foreach (var envVar in image.EnvVars.Where(x => x.Required))
        {
            var hasValue = selection.EnvOverrides?.TryGetValue(envVar.Name, out var value) == true && !string.IsNullOrWhiteSpace(value);
            if (!hasValue && string.IsNullOrWhiteSpace(envVar.DefaultValue))
                findings.Add(BundleFinding.Warning("image.envPlaceholder", $"{envVar.DisplayName} will be emitted as a placeholder.", $"image:{selection.Slug}/env:{envVar.Name}"));
        }

        return image;
    }

    private async Task<IReadOnlyList<ResolvedBundlePackage>> ResolvePackagesAsync(
        IReadOnlyList<BundlePackageSelection> selections,
        Guid? workspaceId,
        List<BundleFinding> findings,
        CancellationToken cancellationToken)
    {
        var packages = new List<ResolvedBundlePackage>();
        for (var index = 0; index < selections.Count; index++)
        {
            var selection = selections[index];
            if (selection.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(selection.PackageId) || string.IsNullOrWhiteSpace(selection.Version))
            {
                findings.Add(BundleFinding.Error("package.invalidSelection", $"Package selection at index {index} requires sourceId, packageId, and version.", $"package:{index}"));
                continue;
            }

            var version = workspaceId.HasValue
                ? await catalog.GetVersionForWorkspaceAsync(workspaceId.Value, selection.SourceId, selection.PackageId, selection.Version, cancellationToken)
                : await catalog.GetVersionAsync(selection.SourceId, selection.PackageId, selection.Version, cancellationToken);
            if (version is null)
            {
                findings.Add(BundleFinding.Error("package.missing", $"{selection.PackageId} {selection.Version} is not indexed or visible.", $"package:{selection.PackageId}"));
                continue;
            }

            if (!version.RuntimeKinds.Any(IsServerRuntimeKind))
            {
                findings.Add(BundleFinding.Error("package.runtimeKindMismatch", $"{selection.PackageId} {selection.Version} is not compatible with Elsa Server.", $"package:{selection.PackageId}"));
                continue;
            }

            var selectedFeatures = NormalizeSelectedFeatures(selection.SelectedFeatures);
            foreach (var featureId in selectedFeatures)
            {
                var feature = version.Features.FirstOrDefault(x => string.Equals(x.FeatureId, featureId, StringComparison.OrdinalIgnoreCase));
                if (feature is null)
                {
                    findings.Add(BundleFinding.Error("feature.missing", $"Feature {featureId} is not present in {selection.PackageId} {selection.Version}.", $"feature:{featureId}"));
                    continue;
                }

                if (!feature.RuntimeKinds.Any(IsServerRuntimeKind))
                    findings.Add(BundleFinding.Error("feature.runtimeKindMismatch", $"Feature {featureId} is not compatible with Elsa Server.", $"feature:{featureId}"));
            }

            var source = new ResolvedPackageSource(version.Source.Id, version.Source.Name, version.Source.Url, "nuget");
            packages.Add(new ResolvedBundlePackage(
                selection.SourceId,
                selection.PackageId,
                selection.Version,
                selectedFeatures,
                NormalizeSettings(selection.Settings),
                version.Features.Select(ToResolvedFeature).ToList(),
                source));
        }

        return packages
            .OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceId)
            .ToList();
    }

    private async Task<IReadOnlyList<ResolvedPackageSource>> ResolveSourcesAsync(
        IReadOnlyList<PackageSourceSelection> selections,
        IReadOnlyList<ResolvedBundlePackage> packages,
        Guid? workspaceId,
        List<BundleFinding> findings,
        CancellationToken cancellationToken)
    {
        var sources = packages.Select(x => x.Source).ToDictionary(x => x.SourceId);
        var requestedSourceIds = selections.Where(x => x.SourceId != Guid.Empty).Select(x => x.SourceId).Distinct().ToList();
        if (requestedSourceIds.Count > 0)
        {
            var visiblePackages = workspaceId.HasValue
                ? await catalog.ListPackagesForWorkspaceAsync(workspaceId.Value, requestedSourceIds, cancellationToken)
                : await catalog.ListPackagesAsync(requestedSourceIds, cancellationToken);
            foreach (var package in visiblePackages)
                sources.TryAdd(package.Source.Id, new ResolvedPackageSource(package.Source.Id, package.Source.Name, package.Source.Url, "nuget"));
        }

        foreach (var selection in selections)
        {
            if (selection.SourceId == Guid.Empty)
            {
                findings.Add(BundleFinding.Error("source.invalidSelection", "Package source selection requires sourceId.", "source"));
                continue;
            }

            if (!sources.ContainsKey(selection.SourceId))
                findings.Add(BundleFinding.Error("source.missing", $"Package source {selection.SourceId} is not indexed or visible.", $"source:{selection.SourceId}"));
        }

        return sources.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceId)
            .ToList();
    }

    private IReadOnlyList<ResolvedInfrastructureProvider> ValidateInfrastructure(IReadOnlyList<InfrastructureSelection> selections, List<BundleFinding> findings)
    {
        var providers = infrastructureProviders.ListProviders();
        var resolved = new List<ResolvedInfrastructureProvider>();
        foreach (var selection in selections)
        {
            var provider = providers.FirstOrDefault(x =>
                string.Equals(x.Id, selection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Kind, selection.Kind, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                findings.Add(BundleFinding.Error("infrastructure.unknownProvider", $"Infrastructure provider {selection.ProviderId} is not known.", $"infrastructure:{selection.ProviderId}"));
                continue;
            }

            if (!string.Equals(provider.Strategy, selection.Strategy, StringComparison.OrdinalIgnoreCase))
                findings.Add(BundleFinding.Error("infrastructure.strategyMismatch", $"Infrastructure provider {provider.Id} does not support strategy {selection.Strategy}.", $"infrastructure:{selection.ProviderId}"));

            resolved.Add(new ResolvedInfrastructureProvider(provider.Id, provider.DisplayName, provider.Kind, provider.Strategy, provider.Provider, provider.Outputs));
        }

        return resolved
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateLocalPackages(LocalPackagesOptions? localPackages, List<BundleFinding> findings)
    {
        if (localPackages is not { Enabled: true })
            return;

        if (string.IsNullOrWhiteSpace(localPackages.DirectoryPath))
        {
            findings.Add(BundleFinding.Error("localPackages.pathRequired", "Local package directory is required when local packages are enabled.", "localPackages"));
            return;
        }

        if (Path.IsPathRooted(localPackages.DirectoryPath) || localPackages.DirectoryPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or ".."))
            findings.Add(BundleFinding.Error("localPackages.invalidPath", "Local package directory must be a relative path inside the bundle.", "localPackages"));
    }

    private static string NormalizeTag(string? tag, RuntimeImage image) =>
        string.IsNullOrWhiteSpace(tag) ? image.DefaultTag : tag.Trim();

    private static IReadOnlyList<string> NormalizeSelectedFeatures(IReadOnlyList<string>? features) =>
        features?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>> NormalizeSettings(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>>? settings) =>
        settings is null
            ? new Dictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>>(StringComparer.OrdinalIgnoreCase)
            : settings.ToDictionary(
                x => x.Key,
                x => (IReadOnlyDictionary<string, System.Text.Json.JsonElement>)x.Value.ToDictionary(y => y.Key, y => y.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    private static ResolvedFeature ToResolvedFeature(PublicFeatureProjection feature) =>
        new(
            feature.FeatureId,
            feature.DisplayName,
            feature.Settings
                .Select(setting => new ResolvedFeatureSetting(
                    setting.Name,
                    setting.DisplayName,
                    setting.Required,
                    setting.Secret,
                    setting.DefaultValueJson,
                    setting.EnvironmentVariable))
                .ToList());

    private static BundleFinding ToBundleFinding(CompatibilityFinding finding) =>
        new(finding.Severity, finding.Code, finding.Message, null);

    private static int RequiredFileSortIndex(string path)
    {
        for (var index = 0; index < BundleFilePolicy.RequiredFilePaths.Count; index++)
        {
            if (string.Equals(BundleFilePolicy.RequiredFilePaths[index], path, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }

    private static bool IsServerRuntimeKind(string runtimeKind) =>
        string.Equals(runtimeKind, ServerRuntimeKind, StringComparison.OrdinalIgnoreCase);
}
