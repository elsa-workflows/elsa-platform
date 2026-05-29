using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed record WorkflowArtifactRuntimeCapability(
    string ArtifactTypeId,
    string RuntimeFamily,
    string? RuntimeVersion,
    IReadOnlyList<string> SupportedSchemaVersions,
    IReadOnlyList<string> Capabilities)
{
    public static WorkflowArtifactRuntimeCapability FromOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        var schemaVersions = Normalize(options.SupportedArtifactSchemaVersions);
        var capabilities = Normalize(options.Capabilities);

        return new WorkflowArtifactRuntimeCapability(
            ArtifactTypeIds.ElsaWorkflowDefinition,
            options.RuntimeFamily.Trim(),
            string.IsNullOrWhiteSpace(options.RuntimeVersion) ? null : options.RuntimeVersion.Trim(),
            schemaVersions,
            capabilities);
    }

    public bool Supports(ArtifactEnvelope envelope) =>
        ArtifactTypeId.Equals(envelope.ArtifactTypeId, StringComparison.OrdinalIgnoreCase)
        && SupportedSchemaVersions.Contains(envelope.ArtifactSchemaVersion, StringComparer.OrdinalIgnoreCase)
        && envelope.CompatibilityHints.Any(SatisfiesCompatibilityHint);

    private bool SatisfiesCompatibilityHint(ArtifactCompatibilityHint hint) =>
        hint.RequiredArtifactType.Equals(ArtifactTypeId, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(hint.RuntimeFamily) || hint.RuntimeFamily.Equals(RuntimeFamily, StringComparison.OrdinalIgnoreCase))
        && WorkflowArtifactRuntimeVersionRange.Includes(hint.RuntimeVersionRange, RuntimeVersion)
        && hint.RequiredCapabilities.All(required => Capabilities.Contains(required, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
