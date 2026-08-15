using ValenceControl.RuntimeBuilder.Abstractions;

namespace ValenceControl.RuntimeBuilder.Core.Builder;

/// <summary>
/// Configuration-defined runtime builder catalog. Runtime images are deployment data rather than
/// compiled constants, so a host can add, retire, or re-tag them without a code change.
/// </summary>
public sealed class RuntimeBuilderOptions
{
    public const string SectionName = "RuntimeBuilder";

    public int PlanTimeoutSeconds { get; set; } = 20;

    public IList<RuntimeImageDefinition> Images { get; set; } = [];

    public IReadOnlyList<RuntimeImage> ToRuntimeImages() => [.. Images.Select(x => x.ToRuntimeImage())];
}

/// <summary>
/// Binding shape for a runtime image. The <see cref="RuntimeImage"/> record cannot be bound directly:
/// the configuration binder cannot construct positional records whose parameters are read-only
/// collections, and it silently produces no images instead of reporting the problem. Mapping through
/// these settable types keeps that failure impossible and keeps the appsettings schema explicit.
/// Values are translated as-is; <see cref="RuntimeImageValidator"/> remains the single judge of what
/// makes a catalog entry valid.
/// </summary>
public sealed class RuntimeImageDefinition
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public IList<string> AvailableTags { get; set; } = [];
    public string DefaultTag { get; set; } = string.Empty;
    public int DefaultPort { get; set; }
    public int HostPort { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string LicenseTier { get; set; } = string.Empty;
    public string Stability { get; set; } = string.Empty;
    public IList<string> Capabilities { get; set; } = [];
    public IList<string> RuntimeKinds { get; set; } = [];
    public IList<RuntimeImageEnvironmentVariableDefinition> EnvVars { get; set; } = [];
    public RuntimeImageDeploymentHintsDefinition DeploymentHints { get; set; } = new();
    public RuntimeImageDocsDefinition Docs { get; set; } = new();

    public RuntimeImage ToRuntimeImage() => new(
        Slug,
        DisplayName,
        Description,
        Image,
        [.. AvailableTags],
        DefaultTag,
        DefaultPort,
        HostPort,
        ContainerName,
        LicenseTier,
        Stability,
        [.. Capabilities],
        [.. RuntimeKinds],
        [.. EnvVars.Select(x => x.ToEnvironmentVariable())],
        DeploymentHints.ToDeploymentHints(),
        Docs.ToDocs());
}

public sealed class RuntimeImageEnvironmentVariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool Secret { get; set; }
    public string? DefaultValue { get; set; }
    public string Group { get; set; } = string.Empty;
    public bool Advanced { get; set; }

    public RuntimeImageEnvironmentVariable ToEnvironmentVariable() =>
        new(Name, DisplayName, Description, Required, Secret, DefaultValue, Group, Advanced);
}

public sealed class RuntimeImageDeploymentHintsDefinition
{
    public bool SupportsDockerCompose { get; set; }
    public bool SupportsKubernetes { get; set; }
    public bool RequiresCompanionServer { get; set; }
    public bool NeedsSharedNetwork { get; set; }
    public string? CompanionImageSlug { get; set; }

    public RuntimeImageDeploymentHints ToDeploymentHints() =>
        new(SupportsDockerCompose, SupportsKubernetes, RequiresCompanionServer, NeedsSharedNetwork, CompanionImageSlug);
}

public sealed class RuntimeImageDocsDefinition
{
    public string? DockerHubUrl { get; set; }
    public IList<string> ContainerPaths { get; set; } = [];
    public bool ShowPerShellAdmin { get; set; }
    public bool ShowNuplane { get; set; }

    public RuntimeImageDocs ToDocs() => new(DockerHubUrl, [.. ContainerPaths], ShowPerShellAdmin, ShowNuplane);
}
