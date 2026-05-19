namespace Elsa.Platform.PackageCatalog.Core.Builder;

public sealed record RuntimeImage(
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
    IReadOnlyList<RuntimeImageEnvironmentVariable> EnvVars,
    RuntimeImageDeploymentHints DeploymentHints,
    RuntimeImageDocs Docs);

public sealed record RuntimeImageEnvironmentVariable(
    string Name,
    string DisplayName,
    string Description,
    bool Required,
    bool Secret,
    string? DefaultValue,
    string Group,
    bool Advanced);

public sealed record RuntimeImageDeploymentHints(
    bool SupportsDockerCompose,
    bool SupportsKubernetes,
    bool RequiresCompanionServer,
    bool NeedsSharedNetwork,
    string? CompanionImageSlug);

public sealed record RuntimeImageDocs(
    string? DockerHubUrl,
    IReadOnlyList<string> ContainerPaths,
    bool ShowPerShellAdmin,
    bool ShowNuplane);
