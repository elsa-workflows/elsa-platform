using Elsa.Platform.PackageCatalog.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Api.Public.Compatibility;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using System.Text.Json;

namespace Elsa.Platform.PackageCatalog.Api.Public.Builder;

public sealed record BuilderCatalogResponse(
    IReadOnlyList<RuntimeImageResponse> Images,
    IReadOnlyList<PublicPackageResponse> Packages,
    IReadOnlyList<BuilderInfrastructureProviderResponse> InfrastructureProviders);

public sealed record RuntimeImageResponse(
    string Slug,
    string DisplayName,
    string Description,
    string Image,
    IReadOnlyList<string> AvailableTags,
    string DefaultTag,
    int DefaultPort,
    int HostPort,
    string ContainerName,
    string LicenseTier,
    string Stability,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RuntimeImageEnvironmentVariableResponse> EnvVars,
    RuntimeImageDeploymentHintsResponse DeploymentHints,
    RuntimeImageDocsResponse Docs);

public sealed record RuntimeImageEnvironmentVariableResponse(
    string Name,
    string DisplayName,
    string Description,
    bool Required,
    bool Secret,
    string? DefaultValue,
    string Group,
    bool Advanced);

public sealed record RuntimeImageDeploymentHintsResponse(
    bool SupportsDockerCompose,
    bool SupportsKubernetes,
    bool RequiresCompanionServer,
    bool NeedsSharedNetwork,
    string? CompanionImageSlug);

public sealed record RuntimeImageDocsResponse(
    string? DockerHubUrl,
    IReadOnlyList<string> ContainerPaths,
    bool ShowPerShellAdmin,
    bool ShowNuplane);

public sealed record BuilderInfrastructureProviderResponse(
    string Id,
    string DisplayName,
    string Kind,
    string Strategy,
    string Provider,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Outputs);

public sealed record BuilderResolveRequest(
    string? ElsaVersion,
    string? DockerImageVersion,
    IReadOnlyList<BuilderSelectedPackageRequest>? Packages,
    IReadOnlyList<string>? Features);

public sealed record BuilderSelectedPackageRequest(
    Guid SourceId,
    string PackageId,
    string Version,
    IReadOnlyList<string>? SelectedFeatures);

public sealed record BuilderResolveResponse(
    bool Compatible,
    IReadOnlyList<CompatibilityFindingApiResponse> Findings);

public sealed record BuilderBundleRequest(
    BuilderBundleImageRequest? Image,
    IReadOnlyList<BuilderBundlePackageRequest>? Packages,
    IReadOnlyList<BuilderBundlePackageSourceRequest>? PackageSources,
    IReadOnlyList<BuilderBundleInfrastructureRequest>? Infrastructure,
    BuilderBundleLocalPackagesRequest? LocalPackages,
    string? Target = null);

public sealed record BuilderBundleImageRequest(
    string? Slug,
    string? Tag,
    int? HostPort,
    IReadOnlyDictionary<string, string>? EnvOverrides);

public sealed record BuilderBundlePackageRequest(
    Guid SourceId,
    string? PackageId,
    string? Version,
    IReadOnlyList<string>? SelectedFeatures,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Settings);

public sealed record BuilderBundlePackageSourceRequest(
    Guid SourceId,
    string? Name,
    string? Url,
    string? Kind);

public sealed record BuilderBundleInfrastructureRequest(
    string? Kind,
    string? ProviderId,
    string? Strategy,
    IReadOnlyDictionary<string, JsonElement>? Settings);

public sealed record BuilderBundleLocalPackagesRequest(
    bool Enabled,
    string? DirectoryPath);

public sealed record BuilderBundleResponse(
    string BundleId,
    IReadOnlyList<BuilderBundleFileResponse> Files,
    IReadOnlyList<BuilderBundleFindingResponse> Findings);

public sealed record BuilderBundleFileResponse(
    string Path,
    string Language,
    string ContentType,
    bool Required,
    string Contents);

public sealed record BuilderBundleFindingResponse(
    string Level,
    string Code,
    string Message,
    string? Scope);

public sealed record BuilderPlanApiRequest(RuntimeBuilderIntent? Intent);

public sealed record BuilderPlanApiResponse(
    RuntimeBuilderIntent Resolved,
    BuilderPlanAutoAddedApiResponse AutoAdded,
    IReadOnlyList<BuilderBundleFindingResponse> Findings);

public sealed record BuilderPlanAutoAddedApiResponse(
    IReadOnlyList<BundlePackageSelection> Packages,
    IReadOnlyList<string> Features,
    IReadOnlyList<InfrastructureSelection> Infrastructure);
