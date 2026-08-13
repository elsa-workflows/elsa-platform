using ValenceControl.Deployment.Abstractions.Artifacts;

namespace ValenceControl.Deployment.Artifacts;

public static class ArtifactEnvelopeConstants
{
    public const string EnvelopeVersion = "valence-control/artifact-envelope/v1alpha1";
    public const string DefaultArtifactSchemaVersion = "1.0";
}

public static class ArtifactTypeIds
{
    public const string ElsaWorkflowDefinition = "elsa.workflow-definition";
    public const string ElsaLoomRecipe = "elsa.loom.recipe";
}

public sealed record ArtifactEnvelope(
    string ArtifactId,
    string EnvelopeVersion,
    string ArtifactTypeId,
    string ArtifactSchemaVersion,
    ArtifactDigest ContentDigest,
    ArtifactDigest? ManifestDigest,
    ArtifactPayloadReference PayloadReference,
    ArtifactProducer Producer,
    ArtifactDisplayMetadata DisplayMetadata,
    IReadOnlyList<ArtifactCompatibilityHint> CompatibilityHints,
    IReadOnlyList<ArtifactEnvelopeDiagnostic> Diagnostics);

public sealed record ArtifactPayloadReference(
    string Provider,
    string Uri,
    string? MediaType = null,
    long? SizeBytes = null,
    ArtifactDigest? ReferenceDigest = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record ArtifactProducer(
    string ProducerType,
    string ProducerName,
    string? ProducerVersion = null,
    string? SourceReference = null);

public sealed record ArtifactDisplayMetadata(
    string? Name,
    string? Version,
    string? Description,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    string? Source = null);

public sealed record ArtifactCompatibilityHint(
    string RequiredArtifactType,
    string? RuntimeFamily,
    string? RuntimeVersionRange,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyDictionary<string, string> EnvironmentConstraints);

public sealed record ArtifactEnvelopeDiagnostic(
    string Code,
    ArtifactEnvelopeDiagnosticSeverity Severity,
    string Message,
    string? Target = null);

public enum ArtifactEnvelopeDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
