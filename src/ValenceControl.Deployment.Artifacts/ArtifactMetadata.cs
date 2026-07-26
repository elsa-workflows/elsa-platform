using System.Text.Json.Serialization;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Resources;

namespace ValenceControl.Deployment.Artifacts;

public sealed record DeploymentArtifactManifestMetadata(
    string? Name,
    string? Version,
    string? Environment,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations);

public sealed record DeploymentArtifactResourceSummary(
    string Type,
    string LogicalId,
    string? Scope,
    string? Version,
    ArtifactDigest? DesiredStateHash)
{
    public static DeploymentArtifactResourceSummary FromResource(DeploymentResource resource) =>
        new(
            resource.Id.Type,
            resource.Id.LogicalId,
            resource.Id.Scope,
            resource.Version,
            resource.DesiredStateHash);
}

public sealed record DeploymentArtifactMetadata(
    string LayoutVersion,
    string ArtifactId,
    DateTimeOffset CreatedAt,
    DeploymentArtifactManifestMetadata Manifest,
    IReadOnlyCollection<DeploymentArtifactResourceSummary> Resources,
    ArtifactDigest ContentDigest,
    string? Builder = null,
    string? Source = null);

public sealed record DeploymentArtifactEntry(
    string Path,
    DeploymentArtifactEntryKind Kind,
    long Size,
    string? SourcePath = null);

public sealed record DeploymentArtifactChecksumEntry(
    string Path,
    DeploymentArtifactEntryKind Kind,
    string Algorithm,
    string Digest,
    long Size);

public sealed record DeploymentArtifactChecksumVerification(
    string Path,
    DeploymentArtifactEntryKind Kind,
    DeploymentArtifactChecksumStatus Status,
    string? ExpectedDigest = null,
    string? ActualDigest = null);

internal sealed record DeploymentArtifactChecksumInventory
{
    [JsonConstructor]
    public DeploymentArtifactChecksumInventory(
        string algorithm,
        IReadOnlyCollection<DeploymentArtifactChecksumEntry>? entries)
    {
        Algorithm = algorithm;
        Entries = entries ?? [];
    }

    public string Algorithm { get; }

    public IReadOnlyCollection<DeploymentArtifactChecksumEntry> Entries { get; }
}
