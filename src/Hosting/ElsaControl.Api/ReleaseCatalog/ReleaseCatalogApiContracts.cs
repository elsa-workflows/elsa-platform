using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;

namespace ElsaControl.Api.ReleaseCatalog;

/// <summary>
/// Server-owned release-catalog admission policy. The HTTP request must never be
/// allowed to override these values.
/// </summary>
public sealed class ReleaseCatalogAdmissionOptions
{
    public const string ConfigurationSection = "ReleaseCatalog:Admission";

    public string ExpectedSignatureSubject { get; set; } = "";

    public string RegistryClass { get; set; } = "paid";

    public string? ExpectedOidcIssuer { get; set; }

    /// <summary>
    /// Control-owned lifecycle state assigned when a producer release is admitted.
    /// It is intentionally separate from the producer's lifecycle declaration.
    /// </summary>
    public string CatalogLifecycle { get; set; } = "Preview";

    public GovernedReleaseCatalogAdmissionOptions ToAdmissionOptions() =>
        new(
            new ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests.ReleaseManifestAdmissionOptions(
                ExpectedSignatureSubject?.Trim() ?? "",
                RegistryClass?.Trim() ?? "",
                TopologyId: null,
                string.IsNullOrWhiteSpace(ExpectedOidcIssuer) ? null : ExpectedOidcIssuer.Trim(),
                AllowLegacySchema: false),
            CatalogLifecycle?.Trim() ?? "");
}

public sealed record AdminReleaseManifestIngestionRequest(
    string? Reference,
    string? Digest,
    string? Payload);

public sealed record AdminReleaseCatalogAdmissionResponse(
    GovernedReleaseCatalogWriteStatus Status,
    IReadOnlyList<ReleaseCatalogEntryResponse> Entries);

public sealed record ReleaseCatalogEntryResponse(
    string SchemaVersion,
    string ManifestReference,
    string ManifestDigest,
    string PayloadDigest,
    string SignatureEvidenceReference,
    string SignatureEvidenceDigest,
    string RegistryClass,
    ReleaseCatalogDistributionResponse Distribution,
    ReleaseCatalogTopologyResponse Topology,
    DateTimeOffset AdmittedAt);

public sealed record ReleaseCatalogDistributionResponse(
    string Id,
    string Generation,
    string ReleaseLine,
    string ReleaseVersion,
    string Channel,
    string ProducerLifecycle,
    string CatalogLifecycle,
    string? Edition,
    ReleaseCatalogSourceResponse Source);

public sealed record ReleaseCatalogSourceResponse(
    string Repository,
    string Commit,
    string RunId);

public sealed record ReleaseCatalogTopologyResponse(
    string Id,
    string PackageManifestSchema,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ReleaseCatalogComponentVersionResponse> ComponentVersions,
    IReadOnlyList<ReleaseCatalogComponentResponse> Components,
    IReadOnlyList<ReleaseCatalogEvidenceResponse> Evidence);

public sealed record ReleaseCatalogComponentVersionResponse(string Id, string Version);

public sealed record ReleaseCatalogComponentResponse(
    string Id,
    string ImageReference,
    string ImageDigest,
    IReadOnlyDictionary<string, string> PlatformDigests,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ReleaseCatalogEndpointResponse> Endpoints,
    string? CompanionComponentId);

public sealed record ReleaseCatalogEndpointResponse(
    string Name,
    string Protocol,
    int Port,
    string Visibility,
    bool RequiresTls,
    string? Path);

public sealed record ReleaseCatalogEvidenceResponse(string Kind, string Reference, string Digest);

public static class ReleaseCatalogApiMappings
{
    public static ReleaseCatalogEntryResponse ToResponse(GovernedReleaseCatalogEntry entry) =>
        new(
            entry.SchemaVersion,
            entry.ManifestReference,
            entry.ManifestDigest,
            entry.PayloadDigest,
            entry.SignatureEvidenceReference,
            entry.SignatureEvidenceDigest,
            entry.RegistryClass,
            new(
                entry.Distribution.Id,
                entry.Distribution.Generation,
                entry.Distribution.ReleaseLine,
                entry.Distribution.ReleaseVersion,
                entry.Distribution.Channel,
                entry.Distribution.ProducerLifecycle,
                entry.CatalogLifecycle,
                entry.Distribution.Edition,
                new(
                    entry.Distribution.SourceRepository,
                    entry.Distribution.SourceCommit,
                    entry.Distribution.SourceRunId)),
            new(
                entry.Topology.Id,
                entry.Topology.PackageManifestSchema,
                entry.Topology.RuntimeKinds,
                entry.Topology.Capabilities,
                entry.Topology.ComponentVersions.Select(x => new ReleaseCatalogComponentVersionResponse(x.Id, x.Version)).ToArray(),
                entry.Topology.Components.Select(x => new ReleaseCatalogComponentResponse(
                    x.Id,
                    x.ImageReference,
                    x.ImageDigest,
                    x.PlatformDigests,
                    x.Roles,
                    x.Capabilities,
                    x.Endpoints.Select(endpoint => new ReleaseCatalogEndpointResponse(
                        endpoint.Name,
                        endpoint.Protocol,
                        endpoint.Port,
                        endpoint.Visibility,
                        endpoint.RequiresTls,
                        endpoint.Path)).ToArray(),
                    x.CompanionComponentId)).ToArray(),
                entry.Topology.Evidence.Select(x => new ReleaseCatalogEvidenceResponse(x.Kind, x.Reference, x.Digest)).ToArray()),
            entry.AdmittedAt);
}
