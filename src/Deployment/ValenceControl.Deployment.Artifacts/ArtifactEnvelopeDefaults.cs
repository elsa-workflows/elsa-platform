using ValenceControl.Deployment.Abstractions.Artifacts;

namespace ValenceControl.Deployment.Artifacts;

/// <summary>
/// Fallback inputs used only to synthesize defaults when a caller has no payload reference or
/// display metadata of its own. Grouping them keeps <see cref="ArtifactEnvelopeDefaults.CreateEnvelope"/>
/// from taking a long run of same-typed positional strings that are easy to transpose.
/// </summary>
public readonly record struct ArtifactEnvelopeFallback(
    string ReferenceProvider,
    string Reference,
    string? ManifestName,
    string? ManifestVersion,
    string? ManifestEnvironment);

/// <summary>
/// Shared bridge for materializing <see cref="ArtifactEnvelope"/> instances from partially populated
/// wire/persistence models. All producers and consumers must agree on the same defaults, most notably
/// the artifact type assumed when a record predates typed envelopes.
/// </summary>
public static class ArtifactEnvelopeDefaults
{
    // The canonical type for an artifact whose type is missing. Producer/registration paths persist a
    // concrete type (the DB column is non-null), so this is only a defensive fallback; consumers that
    // receive untrusted, untyped input should fail rather than assume this default.
    public const string ArtifactTypeId = ArtifactTypeIds.ElsaLoomRecipe;
    public const string RuntimeFamily = "elsa-workflows";

    public static string ArtifactTypeIdOrDefault(string? artifactTypeId) =>
        string.IsNullOrWhiteSpace(artifactTypeId) ? ArtifactTypeId : artifactTypeId.Trim();

    public static ArtifactProducer DefaultProducer() =>
        new("manual", "Manual registration");

    public static ArtifactDisplayMetadata DefaultDisplayMetadata(string? name, string? version, string? environment) =>
        new(name, version, null, new Dictionary<string, string>(), new Dictionary<string, string>(), environment);

    public static ArtifactPayloadReference DefaultPayloadReference(string referenceProvider, string reference) =>
        new(referenceProvider, reference);

    public static IReadOnlyList<ArtifactCompatibilityHint> DefaultCompatibilityHints(string? artifactTypeId)
    {
        var typeId = ArtifactTypeIdOrDefault(artifactTypeId);
        return [new ArtifactCompatibilityHint(typeId, RuntimeFamily, null, [ArtifactApplyCapability.For(typeId)], new Dictionary<string, string>())];
    }

    public static IReadOnlyList<ArtifactCompatibilityHint> NormalizeCompatibilityHints(
        string? artifactTypeId,
        IReadOnlyList<ArtifactCompatibilityHint>? compatibilityHints) =>
        (compatibilityHints ?? DefaultCompatibilityHints(artifactTypeId))
            .Select(hint => hint with
            {
                RequiredCapabilities = hint.RequiredCapabilities
                    .Select(capability => ArtifactApplyCapability.Normalize(hint.RequiredArtifactType, capability))
                    .Where(capability => !string.IsNullOrWhiteSpace(capability))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

    public static ArtifactEnvelope CreateEnvelope(
        string artifactId,
        string? envelopeVersion,
        string? artifactTypeId,
        string? artifactSchemaVersion,
        ArtifactDigest contentDigest,
        ArtifactDigest? manifestDigest,
        ArtifactPayloadReference? payloadReference,
        ArtifactProducer? producer,
        ArtifactDisplayMetadata? displayMetadata,
        IReadOnlyList<ArtifactCompatibilityHint>? compatibilityHints,
        IReadOnlyList<ArtifactEnvelopeDiagnostic> diagnostics,
        ArtifactEnvelopeFallback fallback)
    {
        var typeId = ArtifactTypeIdOrDefault(artifactTypeId);
        return new ArtifactEnvelope(
            artifactId,
            string.IsNullOrWhiteSpace(envelopeVersion) ? ArtifactEnvelopeConstants.EnvelopeVersion : envelopeVersion.Trim(),
            typeId,
            string.IsNullOrWhiteSpace(artifactSchemaVersion) ? ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion : artifactSchemaVersion.Trim(),
            contentDigest,
            manifestDigest,
            payloadReference ?? DefaultPayloadReference(fallback.ReferenceProvider, fallback.Reference),
            producer ?? DefaultProducer(),
            displayMetadata ?? DefaultDisplayMetadata(fallback.ManifestName, fallback.ManifestVersion, fallback.ManifestEnvironment),
            NormalizeCompatibilityHints(typeId, compatibilityHints),
            diagnostics);
    }
}
