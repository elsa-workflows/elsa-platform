using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;

namespace ValenceControl.PackageCatalog.Core.Compatibility;

public static class RuntimeKindCompatibilityPolicy
{
    public static IReadOnlyList<string> ResolvePackageRuntimeKinds(ElsaPackageManifest? manifest) =>
        Normalize(RuntimeKindCompatibility.EffectiveRuntimeKinds(manifest?.Compatibility?.RuntimeKinds));

    public static IReadOnlyList<string> ResolveFeatureRuntimeKinds(FeatureManifest? feature, IReadOnlyList<string> packageRuntimeKinds) =>
        Normalize(feature?.Compatibility?.RuntimeKinds is { Count: > 0 }
            ? feature.Compatibility.RuntimeKinds
            : packageRuntimeKinds);

    public static bool IsCompatibleWith(IReadOnlyList<string> runtimeKinds, string targetRuntimeKind) =>
        RuntimeKindCompatibility.Contains(runtimeKinds, targetRuntimeKind);

    public static IReadOnlyList<Abstractions.Catalog.PublicPackageProjection> FilterPackages(
        IReadOnlyList<Abstractions.Catalog.PublicPackageProjection> packages,
        string targetRuntimeKind) =>
        packages
            .Select(package => FilterPackage(package, targetRuntimeKind))
            .Where(package => package.Versions.Count > 0)
            .ToList();

    private static Abstractions.Catalog.PublicPackageProjection FilterPackage(
        Abstractions.Catalog.PublicPackageProjection package,
        string targetRuntimeKind)
    {
        var versions = package.Versions
            .Where(version => IsCompatibleWith(version.RuntimeKinds, targetRuntimeKind))
            .Select(version => version with
            {
                Features = version.Features
                    .Where(feature => IsCompatibleWith(feature.RuntimeKinds, targetRuntimeKind))
                    .ToList()
            })
            .ToList();

        var runtimeKinds = versions
            .SelectMany(version => version.RuntimeKinds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return package with { RuntimeKinds = runtimeKinds, Versions = versions };
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> runtimeKinds) =>
        runtimeKinds
            .Where(RuntimeKindCompatibility.IsValid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
