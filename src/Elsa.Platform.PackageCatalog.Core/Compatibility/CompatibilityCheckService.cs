using System.Text.Json;
using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageManifests;
using Elsa.Platform.PackageManifests.Compatibility;

namespace Elsa.Platform.PackageCatalog.Core.Compatibility;

public sealed class CompatibilityCheckService(ICompatibilityQueries queries, VersionRangeEvaluator ranges) : IPackageCompatibilityService
{
    public async Task<CompatibilityCheckResult> CheckAsync(CompatibilityCheckRequest request, CancellationToken cancellationToken = default)
    {
        var findings = new List<CompatibilityFinding>();
        var selected = new List<(SelectedPackageIdentity Identity, ElsaPackageManifest Manifest)>();

        var validPackages = new List<SelectedPackageVersion>();
        for (var index = 0; index < request.Packages.Count; index++)
        {
            var package = request.Packages[index];
            if (package.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.Version))
            {
                findings.Add(CompatibilityFinding.Error("package.invalidSelection", $"Package selection at index {index} requires sourceId, packageId, and version."));
                continue;
            }

            validPackages.Add(package);
        }

        foreach (var package in validPackages)
        {
            var version = await queries.GetPackageVersionAsync(request.WorkspaceId, package.SourceId, package.PackageId, package.Version, cancellationToken);
            if (version is null)
            {
                findings.Add(CompatibilityFinding.Error("package.missing", $"{package.PackageId} {package.Version} is not indexed."));
                continue;
            }

            if (version.Package is not { Approved: true, Listed: true } || !version.IsListed || version.ApprovalStatus != PackageApprovalStatus.Approved)
                findings.Add(CompatibilityFinding.Error("package.notApproved", $"{package.PackageId} {package.Version} is not approved and listed."));

            if (version.SuspiciousChangeDetected)
                findings.Add(CompatibilityFinding.Error("package.suspicious", $"{package.PackageId} {package.Version} has a suspicious manifest change."));

            if (version.ValidationStatus != ValidationStatus.Valid)
            {
                findings.Add(CompatibilityFinding.Error("package.invalid", $"{package.PackageId} {package.Version} does not have a valid manifest."));
                continue;
            }

            if (!TryParseManifest(version.ManifestJson, out var manifest))
            {
                findings.Add(CompatibilityFinding.Error("manifest.invalidJson", $"{package.PackageId} {package.Version} has invalid manifest JSON."));
                continue;
            }

            selected.Add((new SelectedPackageIdentity(package.SourceId, package.PackageId), manifest!));

            if (manifest?.Compatibility?.ElsaVersionRange is { } elsaRange && !ranges.Includes(elsaRange, request.ElsaVersion))
                findings.Add(CompatibilityFinding.Error("compatibility.elsa", $"{package.PackageId} {package.Version} is not compatible with Elsa {request.ElsaVersion}."));

            if (manifest?.Compatibility?.DockerImageVersionRange is { } dockerRange && !ranges.Includes(dockerRange, request.DockerImageVersion))
                findings.Add(CompatibilityFinding.Error("compatibility.docker", $"{package.PackageId} {package.Version} is not compatible with Docker image {request.DockerImageVersion}."));
        }

        var selectedVersions = validPackages
            .GroupBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(package => package.Version).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var (_, manifest) in selected)
        {
            foreach (var conflict in manifest.Conflicts)
            {
                if (conflict.PackageId is not null
                    && selectedVersions.TryGetValue(conflict.PackageId, out var conflictingVersions)
                    && conflictingVersions.Any(selectedVersion => ranges.Includes(conflict.VersionRange, selectedVersion)))
                {
                    findings.Add(CompatibilityFinding.Error("package.conflict", $"{manifest.Package.Id} conflicts with {conflict.PackageId}."));
                }
            }
        }

        if (request.Features.Count > 0)
            ValidateSelectedFeatures(request.Features, selected, selectedVersions, ranges, request.RuntimeKinds, findings);

        return new CompatibilityCheckResult(findings.Count == 0, findings);
    }

    private static void ValidateSelectedFeatures(
        IReadOnlyList<string> selectedFeatureIds,
        IReadOnlyList<(SelectedPackageIdentity Identity, ElsaPackageManifest Manifest)> selected,
        IReadOnlyDictionary<string, List<string>> selectedVersions,
        VersionRangeEvaluator ranges,
        IReadOnlyList<string>? runtimeKinds,
        List<CompatibilityFinding> findings)
    {
        var selectedFeatures = new HashSet<string>(selectedFeatureIds, StringComparer.OrdinalIgnoreCase);
        var features = selected
            .SelectMany(package => package.Manifest.Features.Select(feature => new SelectedFeatureManifest(package.Manifest.Package.Id, package.Manifest.Package.Version, package.Manifest.Compatibility, feature)))
            .Where(x => selectedFeatures.Any(selectedFeature => FeatureMatchesIdentity(x.Feature, selectedFeature)))
            .ToList();

        foreach (var requestedFeatureId in selectedFeatures)
        {
            if (features.All(x => !FeatureMatchesIdentity(x.Feature, requestedFeatureId)))
                findings.Add(CompatibilityFinding.Error("feature.missing", $"Feature {requestedFeatureId} is not present in the selected packages."));
        }

        foreach (var selectedFeature in features)
        {
            var feature = selectedFeature.Feature;
            var effectiveRuntimeKinds = EffectiveRuntimeKinds(selectedFeature.PackageCompatibility, feature);
            if (!RuntimeKindCompatibilityPolicy.IsCompatible(effectiveRuntimeKinds, runtimeKinds))
                findings.Add(CompatibilityFinding.Error("feature.runtimeKindUnsupported", $"{feature.Id} is not compatible with the selected runtime image."));

            foreach (var dependency in feature.Dependencies.Where(x => !x.Optional))
            {
                if (dependency.PackageId is not null && !PackageMatches(dependency.PackageId, dependency.VersionRange, selectedVersions, ranges))
                {
                    findings.Add(CompatibilityFinding.Error("feature.packageDependency", $"{feature.Id} requires package {dependency.PackageId}."));
                    continue;
                }

                if (dependency.FeatureId is not null && !FeatureMatches(dependency.PackageId, dependency.VersionRange, dependency.FeatureId, features, ranges))
                    findings.Add(CompatibilityFinding.Error("feature.dependency", $"{feature.Id} requires feature {dependency.FeatureId}."));
            }

            foreach (var conflict in feature.Conflicts)
            {
                if (conflict.PackageId is not null && !PackageMatches(conflict.PackageId, conflict.VersionRange, selectedVersions, ranges))
                    continue;

                if (conflict.FeatureId is null || FeatureMatches(conflict.PackageId, conflict.VersionRange, conflict.FeatureId, features, ranges))
                    findings.Add(CompatibilityFinding.Error("feature.conflict", $"{feature.Id} conflicts with feature {conflict.FeatureId}."));
            }
        }
    }

    private static bool PackageMatches(string packageId, string? versionRange, IReadOnlyDictionary<string, List<string>> selectedVersions, VersionRangeEvaluator ranges) =>
        selectedVersions.TryGetValue(packageId, out var versions) && versions.Any(version => ranges.Includes(versionRange, version));

    private static IReadOnlyList<string> EffectiveRuntimeKinds(CompatibilityManifest? packageCompatibility, FeatureManifest feature)
    {
        var featureRuntimeKinds = RuntimeKindCompatibilityPolicy.Normalize(feature.Compatibility?.RuntimeKinds);
        return featureRuntimeKinds.Count > 0
            ? featureRuntimeKinds
            : RuntimeKindCompatibilityPolicy.Normalize(packageCompatibility?.RuntimeKinds);
    }

    private static bool FeatureMatches(string? packageId, string? versionRange, string featureId, IReadOnlyList<SelectedFeatureManifest> features, VersionRangeEvaluator ranges) =>
        features.Any(feature =>
            FeatureMatchesIdentity(feature.Feature, featureId)
            && (packageId is null || string.Equals(feature.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            && (packageId is null || ranges.Includes(versionRange, feature.PackageVersion)));

    private static bool FeatureMatchesIdentity(FeatureManifest feature, string featureId) =>
        FeatureDependencyIdentityPolicy.Matches(feature.Id, ShellFeatureName(feature), featureId);

    private static string? ShellFeatureName(FeatureManifest feature)
    {
        return feature.Extensions.TryGetValue("cshellsFeatureName", out var value)
            ? value switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                _ => value?.ToString()
            }
            : null;
    }

    private static bool TryParseManifest(string manifestJson, out ElsaPackageManifest? manifest)
    {
        try
        {
            manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(manifestJson, ManifestJsonSerializerOptions.Default);
            return manifest is not null;
        }
        catch (JsonException)
        {
            manifest = null;
            return false;
        }
    }
}

public interface ICompatibilityQueries
{
    Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default);
}

internal sealed record SelectedPackageIdentity(Guid SourceId, string PackageId);

internal sealed record SelectedFeatureManifest(string PackageId, string PackageVersion, CompatibilityManifest? PackageCompatibility, FeatureManifest Feature)
{
    public string Id => Feature.Id;
}
