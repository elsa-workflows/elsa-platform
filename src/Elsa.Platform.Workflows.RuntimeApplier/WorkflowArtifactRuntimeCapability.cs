using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed record WorkflowArtifactRuntimeCapability(
    IReadOnlyList<string> SupportedArtifactTypes,
    string RuntimeFamily,
    string? RuntimeVersion,
    IReadOnlyList<string> SupportedSchemaVersions,
    IReadOnlyList<string> Capabilities)
{
    private static readonly IReadOnlyList<string> ApplicableArtifactTypes =
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
            ApplicableArtifactTypes,
            options.RuntimeFamily.Trim(),
            options.RuntimeVersion.Trim(),
            schemaVersions,
            ExpandApplyCapabilities(capabilities));
    }

    public bool SupportsArtifactType(string artifactTypeId) =>
        SupportedArtifactTypes.Contains(artifactTypeId, StringComparer.OrdinalIgnoreCase);

    public bool Supports(ArtifactEnvelope envelope) =>
        SupportsArtifactType(envelope.ArtifactTypeId)
        && SupportedSchemaVersions.Contains(envelope.ArtifactSchemaVersion, StringComparer.OrdinalIgnoreCase)
        && envelope.CompatibilityHints.Any(SatisfiesCompatibilityHint);

    private bool SatisfiesCompatibilityHint(ArtifactCompatibilityHint hint) =>
        SupportsArtifactType(hint.RequiredArtifactType)
        && (string.IsNullOrWhiteSpace(hint.RuntimeFamily) || hint.RuntimeFamily.Equals(RuntimeFamily, StringComparison.OrdinalIgnoreCase))
        && WorkflowArtifactRuntimeVersionRange.Includes(hint.RuntimeVersionRange, RuntimeVersion)
        && hint.RequiredCapabilities.All(required =>
            Capabilities.Contains(ArtifactApplyCapability.Normalize(hint.RequiredArtifactType, required), StringComparer.OrdinalIgnoreCase));

    // Advertise each configured capability alongside its canonical `artifact.<type>.apply` form,
    // so hints comparing against either spelling resolve. Normalize only rewrites a capability for
    // the artifact type it belongs to and echoes it back otherwise, so at most one extra form is added.
    private static IReadOnlyList<string> ExpandApplyCapabilities(IReadOnlyList<string> capabilities) =>
        capabilities
            .SelectMany(NormalizedForms)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> NormalizedForms(string capability)
    {
        yield return capability;
        foreach (var typeId in ApplicableArtifactTypes)
        {
            var normalized = ArtifactApplyCapability.Normalize(typeId, capability);
            if (!normalized.Equals(capability, StringComparison.OrdinalIgnoreCase))
                yield return normalized;
        }
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
