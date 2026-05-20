using Elsa.Platform.Deployment.Manifest;

namespace Elsa.Platform.Deployment.Artifacts;

public sealed record DeploymentArtifactBuildOptions
{
    public DeploymentArtifactBuildOptions(
        string manifestText,
        ManifestFormat manifestFormat,
        string workspaceRoot,
        string outputPath,
        bool overwrite = false,
        DateTimeOffset? builtAt = null,
        string? builder = null,
        string? source = null)
    {
        ManifestText = manifestText;
        ManifestFormat = manifestFormat;
        WorkspaceRoot = workspaceRoot;
        OutputPath = outputPath;
        Overwrite = overwrite;
        BuiltAt = builtAt?.ToUniversalTime();
        Builder = string.IsNullOrWhiteSpace(builder) ? null : builder.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    public string ManifestText { get; init; }

    public ManifestFormat ManifestFormat { get; init; }

    public string WorkspaceRoot { get; init; }

    public string OutputPath { get; init; }

    public bool Overwrite { get; init; }

    public DateTimeOffset? BuiltAt { get; init; }

    public string? Builder { get; init; }

    public string? Source { get; init; }
}
