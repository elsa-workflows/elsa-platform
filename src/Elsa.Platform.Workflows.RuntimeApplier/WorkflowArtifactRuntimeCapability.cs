using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed record WorkflowArtifactRuntimeCapability(
    string ArtifactTypeId,
    string RuntimeFamily,
    string? RuntimeVersion,
    IReadOnlyList<string> SupportedSchemaVersions,
    IReadOnlyList<string> Capabilities)
{
    private static readonly string[] KnownArtifactTypeIds =
    [
        ArtifactTypeIds.ElsaWorkflowDefinition,
        ArtifactTypeIds.ElsaLoomRecipe
    ];

    public static WorkflowArtifactRuntimeCapability FromOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(options.RuntimeVersion))
            throw new InvalidOperationException("Runtime version is required before advertising artifact capabilities.");

        var schemaVersions = Normalize(options.SupportedArtifactSchemaVersions);
        var capabilities = Normalize(options.Capabilities);

        return new WorkflowArtifactRuntimeCapability(
            ArtifactTypeIds.ElsaWorkflowDefinition,
            options.RuntimeFamily.Trim(),
            options.RuntimeVersion.Trim(),
            schemaVersions,
            capabilities);
    }

    public bool Supports(ArtifactEnvelope envelope) =>
        SupportsArtifactType(envelope.ArtifactTypeId)
        && SupportedSchemaVersions.Contains(envelope.ArtifactSchemaVersion, StringComparer.OrdinalIgnoreCase)
        && envelope.CompatibilityHints.Any(SatisfiesCompatibilityHint);

    public bool SupportsArtifactType(string artifactTypeId) =>
        KnownArtifactTypeIds.Contains(artifactTypeId, StringComparer.OrdinalIgnoreCase)
        && NormalizedCapabilities(artifactTypeId).Contains(ArtifactApplyCapability.For(artifactTypeId), StringComparer.OrdinalIgnoreCase);

    private bool SatisfiesCompatibilityHint(ArtifactCompatibilityHint hint) =>
        SupportsArtifactType(hint.RequiredArtifactType)
        && (string.IsNullOrWhiteSpace(hint.RuntimeFamily) || hint.RuntimeFamily.Equals(RuntimeFamily, StringComparison.OrdinalIgnoreCase))
        && WorkflowArtifactRuntimeVersionRange.Includes(hint.RuntimeVersionRange, RuntimeVersion)
        && hint.RequiredCapabilities.All(required =>
            NormalizedCapabilities(hint.RequiredArtifactType).Contains(
                ArtifactApplyCapability.Normalize(hint.RequiredArtifactType, required),
                StringComparer.OrdinalIgnoreCase));

    private IEnumerable<string> NormalizedCapabilities(string artifactTypeId) =>
        Capabilities.Select(capability => ArtifactApplyCapability.Normalize(artifactTypeId, capability));

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
