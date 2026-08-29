namespace ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

/// <summary>
/// The producer-owned release-manifest schema is independent from Elsa package versions.
/// </summary>
public static class ReleaseManifestSchema
{
    public const string CurrentVersion = "1";
}

public sealed record CommercialReleaseManifest(
    string SchemaVersion,
    ReleaseManifestDistribution Distribution,
    IReadOnlyList<ReleaseManifestTopology> Topologies);

public sealed record ReleaseManifestDistribution(
    string Id,
    string Generation,
    string ReleaseLine,
    string ReleaseVersion,
    string Channel,
    string Lifecycle,
    ReleaseManifestSource Source);

public sealed record ReleaseManifestSource(
    string Repository,
    string Commit,
    string Workflow,
    string RunId);

public sealed record ReleaseManifestTopology(
    string Id,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<ReleaseManifestImage> Images,
    IReadOnlyDictionary<string, string> Components,
    IReadOnlyDictionary<string, string> Endpoints,
    ReleaseManifestCompatibility Compatibility,
    ReleaseManifestSupplyChain SupplyChain);

/// <summary>
/// An image can optionally identify a component. The v1 producer shape may use one
/// image per topology and omit that identity; the projector then uses the topology id.
/// </summary>
public sealed record ReleaseManifestImage(
    string RegistryClass,
    string Reference,
    string IndexDigest,
    IReadOnlyDictionary<string, string>? PlatformDigests = null,
    string? ComponentId = null,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<ReleaseManifestEndpoint>? Endpoints = null,
    string? CompanionComponentId = null);

public sealed record ReleaseManifestEndpoint(
    string Name,
    string Protocol,
    int Port,
    string Visibility,
    bool RequiresTls,
    string? Path = null);

public sealed record ReleaseManifestCompatibility(
    string PackageManifestSchema,
    IReadOnlyList<string> RuntimeCapabilities);

public sealed record ReleaseManifestSupplyChain(
    ReleaseManifestAttestation? Sbom,
    ReleaseManifestAttestation? Provenance,
    IReadOnlyList<ReleaseManifestSignatureEvidence> Signatures,
    ReleaseManifestVulnerabilityScan? VulnerabilityScan);

public sealed record ReleaseManifestAttestation(
    string Uri,
    string Digest);

public sealed record ReleaseManifestSignatureEvidence(
    string RegistryClass,
    string Identity,
    string Uri,
    string? Digest = null);

public sealed record ReleaseManifestVulnerabilityScan(
    string Tool,
    string Policy,
    string Report,
    string? Digest = null);

/// <summary>
/// An immutable artifact envelope. Payload is used only at the ingestion boundary and
/// is never copied into a catalog record or resolved plan.
/// </summary>
public sealed record ReleaseManifestArtifact(
    string Reference,
    string Digest,
    string Payload);

/// <summary>
/// Cryptographic verification is deliberately a seam: registry/cosign implementations
/// can be supplied by the host without coupling this contract to a credential provider.
/// </summary>
public sealed record ReleaseManifestSignatureVerification(
    bool IsValid,
    string Subject,
    string SubjectDigest,
    string EvidenceReference,
    string EvidenceDigest);

public interface IReleaseManifestSignatureVerifier
{
    ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(
        ReleaseManifestArtifact artifact,
        CancellationToken cancellationToken = default);
}

public sealed record ReleaseManifestAdmissionOptions(
    string ExpectedSignatureSubject,
    string RegistryClass = "paid",
    string? TopologyId = null);

public sealed record ReleaseManifestAdmissionFinding(
    string Code,
    string Message,
    string Scope);

/// <summary>
/// Safe retained evidence from a verified signature. Signer identity is intentionally
/// kept inside the verifier boundary and is not projected into a resolved plan.
/// </summary>
public sealed record ReleaseManifestAdmissionEvidence(
    string Reference,
    string Digest);

public sealed record ReleaseManifestAdmissionResult(
    bool Accepted,
    string? Reference,
    string? Digest,
    CommercialReleaseManifest? Manifest,
    ReleaseManifestAdmissionEvidence? SignatureEvidence,
    string RegistryClass,
    string? TopologyId,
    IReadOnlyList<ReleaseManifestAdmissionFinding> Findings);

public static class ReleaseManifestEvidenceKinds
{
    public const string Manifest = "release-manifest";
    public const string Signature = "release-manifest-signature";
    public const string Sbom = "release-manifest-sbom";
    public const string Provenance = "release-manifest-provenance";
    public const string VulnerabilityScan = "release-manifest-vulnerability-scan";
}
