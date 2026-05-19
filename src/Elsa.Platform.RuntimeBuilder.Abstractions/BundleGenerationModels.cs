using System.Text.Json;

namespace Elsa.Platform.RuntimeBuilder.Abstractions;

public sealed record RuntimeBuilderIntent(
    RuntimeImageSelection Image,
    IReadOnlyList<BundlePackageSelection> Packages,
    IReadOnlyList<PackageSourceSelection> PackageSources,
    IReadOnlyList<InfrastructureSelection> Infrastructure,
    LocalPackagesOptions? LocalPackages,
    string? Target = null);

public sealed record RuntimeImageSelection(
    string Slug,
    string? Tag,
    int? HostPort,
    IReadOnlyDictionary<string, string>? EnvOverrides);

public sealed record BundlePackageSelection(
    Guid SourceId,
    string PackageId,
    string Version,
    IReadOnlyList<string>? SelectedFeatures,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Settings);

public sealed record PackageSourceSelection(
    Guid SourceId,
    string? Name = null,
    string? Url = null,
    string? Kind = null);

public sealed record InfrastructureSelection(
    string Kind,
    string ProviderId,
    string Strategy,
    IReadOnlyDictionary<string, JsonElement>? Settings);

public sealed record LocalPackagesOptions(
    bool Enabled,
    string? DirectoryPath);

public sealed record BundleGenerationContext(
    RuntimeBuilderIntent Intent,
    RuntimeImage RuntimeImage,
    string ImageTag,
    int HostPort,
    IReadOnlyList<ResolvedBundlePackage> Packages,
    IReadOnlyList<ResolvedPackageSource> PackageSources,
    IReadOnlyList<ResolvedInfrastructureProvider> Infrastructure);

public sealed record ResolvedBundlePackage(
    Guid SourceId,
    string PackageId,
    string Version,
    IReadOnlyList<string> SelectedFeatures,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Settings,
    IReadOnlyList<ResolvedFeature> Features,
    ResolvedPackageSource Source);

public sealed record ResolvedFeature(
    string Id,
    string DisplayName,
    IReadOnlyList<ResolvedFeatureSetting> Settings);

public sealed record ResolvedFeatureSetting(
    string Name,
    string DisplayName,
    bool Required,
    bool Secret,
    string? DefaultValueJson,
    string? EnvironmentVariable);

public sealed record ResolvedPackageSource(
    Guid SourceId,
    string Name,
    string Url,
    string Kind);

public sealed record ResolvedInfrastructureProvider(
    string Id,
    string DisplayName,
    string Kind,
    string Strategy,
    string Provider,
    IReadOnlyList<string> Outputs);

public sealed record BundleGenerationResult(
    string BundleId,
    IReadOnlyList<BundleFile> Files,
    IReadOnlyList<BundleFinding> Findings);

public sealed record BundleFile(
    string Path,
    string Language,
    string ContentType,
    bool Required,
    string Contents);

public sealed record BundleFinding(
    string Level,
    string Code,
    string Message,
    string? Scope)
{
    public static BundleFinding Error(string code, string message, string? scope = null) => new("error", code, message, scope);
    public static BundleFinding Warning(string code, string message, string? scope = null) => new("warning", code, message, scope);
    public static BundleFinding Info(string code, string message, string? scope = null) => new("info", code, message, scope);
}
