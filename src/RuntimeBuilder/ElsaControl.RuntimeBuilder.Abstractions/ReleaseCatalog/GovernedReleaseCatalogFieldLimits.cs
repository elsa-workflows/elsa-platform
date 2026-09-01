namespace ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;

/// <summary>
/// Maximum lengths for string values persisted by the governed release catalog.
/// Keeping these limits with the catalog contract lets admission validate values
/// before they reach a provider-specific store.
/// </summary>
public static class GovernedReleaseCatalogFieldLimits
{
    public const int SchemaVersion = 64;
    public const int ManifestReference = 2048;
    public const int ManifestDigest = 71;
    public const int PayloadDigest = 71;
    public const int SignatureEvidenceReference = 2048;
    public const int SignatureEvidenceDigest = 71;
    public const int RegistryClass = 64;
    public const int DistributionId = 200;
    public const int Generation = 200;
    public const int ReleaseLine = 100;
    public const int ReleaseVersion = 128;
    public const int Channel = 64;
    public const int ProducerLifecycle = 64;
    public const int Edition = 64;
    public const int SourceRepository = 2048;
    public const int SourceCommit = 128;
    public const int SourceRunId = 128;
    public const int CatalogLifecycle = 64;
    public const int ComponentDeclarationsFormat = 64;
    public const int ComponentDeclarationsDigest = 71;
    public const int ReleasePackageId = 256;
    public const int ReleasePackageVersion = 256;
    public const int TopologyId = 200;
    public const int PackageManifestSchema = 128;
    public const int RuntimeKind = 200;
    public const int Capability = 200;
    public const int ComponentId = 200;
    public const int ComponentVersion = 128;
    public const int ImageReference = 2048;
    public const int ImageDigest = 71;
    public const int CompanionComponentId = 200;
    public const int Platform = 128;
    public const int PlatformDigest = 71;
    public const int Role = 200;
    public const int EndpointName = 200;
    public const int EndpointProtocol = 32;
    public const int EndpointVisibility = 64;
    public const int EndpointPath = 2048;
    public const int EvidenceKind = 64;
    public const int EvidenceReference = 2048;
    public const int EvidenceDigest = 71;
}

/// <summary>
/// Provider-independent validation for every string persisted by the governed
/// release catalog. Stores and ingestion paths share this contract so provider
/// differences cannot turn an accepted projection into truncation or a 500.
/// </summary>
public static class GovernedReleaseCatalogStorageContract
{
    public static void ValidateLengths(GovernedReleaseCatalogEntry entry)
    {
        Validate(entry.SchemaVersion, GovernedReleaseCatalogFieldLimits.SchemaVersion, "schemaVersion");
        Validate(entry.ManifestReference, GovernedReleaseCatalogFieldLimits.ManifestReference, "manifestReference");
        Validate(entry.ManifestDigest, GovernedReleaseCatalogFieldLimits.ManifestDigest, "manifestDigest");
        Validate(entry.PayloadDigest, GovernedReleaseCatalogFieldLimits.PayloadDigest, "payloadDigest");
        Validate(entry.SignatureEvidenceReference, GovernedReleaseCatalogFieldLimits.SignatureEvidenceReference, "signatureEvidenceReference");
        Validate(entry.SignatureEvidenceDigest, GovernedReleaseCatalogFieldLimits.SignatureEvidenceDigest, "signatureEvidenceDigest");
        Validate(entry.RegistryClass, GovernedReleaseCatalogFieldLimits.RegistryClass, "registryClass");
        Validate(entry.CatalogLifecycle, GovernedReleaseCatalogFieldLimits.CatalogLifecycle, "catalogLifecycle");
        if (entry.ComponentDeclarations is { } declarations)
        {
            Validate(declarations.Format, GovernedReleaseCatalogFieldLimits.ComponentDeclarationsFormat, "componentDeclarations.format");
            Validate(declarations.Digest, GovernedReleaseCatalogFieldLimits.ComponentDeclarationsDigest, "componentDeclarations.digest");
            foreach (var package in declarations.Packages ?? [])
            {
                if (package is null)
                    continue;
                Validate(package.Id, GovernedReleaseCatalogFieldLimits.ReleasePackageId, "componentDeclarations.package.id");
                Validate(package.Version, GovernedReleaseCatalogFieldLimits.ReleasePackageVersion, "componentDeclarations.package.version");
            }
        }

        var distribution = entry.Distribution;
        Validate(distribution.Id, GovernedReleaseCatalogFieldLimits.DistributionId, "distribution.id");
        Validate(distribution.Generation, GovernedReleaseCatalogFieldLimits.Generation, "distribution.generation");
        Validate(distribution.ReleaseLine, GovernedReleaseCatalogFieldLimits.ReleaseLine, "distribution.releaseLine");
        Validate(distribution.ReleaseVersion, GovernedReleaseCatalogFieldLimits.ReleaseVersion, "distribution.releaseVersion");
        Validate(distribution.Channel, GovernedReleaseCatalogFieldLimits.Channel, "distribution.channel");
        Validate(distribution.ProducerLifecycle, GovernedReleaseCatalogFieldLimits.ProducerLifecycle, "distribution.producerLifecycle");
        Validate(distribution.Edition, GovernedReleaseCatalogFieldLimits.Edition, "distribution.edition");
        Validate(distribution.SourceRepository, GovernedReleaseCatalogFieldLimits.SourceRepository, "distribution.sourceRepository");
        Validate(distribution.SourceCommit, GovernedReleaseCatalogFieldLimits.SourceCommit, "distribution.sourceCommit");
        Validate(distribution.SourceRunId, GovernedReleaseCatalogFieldLimits.SourceRunId, "distribution.sourceRunId");

        var topology = entry.Topology;
        Validate(topology.Id, GovernedReleaseCatalogFieldLimits.TopologyId, "topology.id");
        Validate(topology.PackageManifestSchema, GovernedReleaseCatalogFieldLimits.PackageManifestSchema, "topology.packageManifestSchema");
        foreach (var runtimeKind in topology.RuntimeKinds)
            Validate(runtimeKind, GovernedReleaseCatalogFieldLimits.RuntimeKind, "topology.runtimeKind");
        foreach (var capability in topology.Capabilities)
            Validate(capability, GovernedReleaseCatalogFieldLimits.Capability, "topology.capability");
        foreach (var componentVersion in topology.ComponentVersions)
        {
            Validate(componentVersion.Id, GovernedReleaseCatalogFieldLimits.ComponentId, "topology.componentVersion.id");
            Validate(componentVersion.Version, GovernedReleaseCatalogFieldLimits.ComponentVersion, "topology.componentVersion.version");
        }

        foreach (var component in topology.Components)
        {
            Validate(component.Id, GovernedReleaseCatalogFieldLimits.ComponentId, "component.id");
            Validate(component.ImageReference, GovernedReleaseCatalogFieldLimits.ImageReference, "component.imageReference");
            Validate(component.ImageDigest, GovernedReleaseCatalogFieldLimits.ImageDigest, "component.imageDigest");
            Validate(component.CompanionComponentId, GovernedReleaseCatalogFieldLimits.CompanionComponentId, "component.companionComponentId");
            foreach (var platform in component.PlatformDigests)
            {
                Validate(platform.Key, GovernedReleaseCatalogFieldLimits.Platform, "component.platform");
                Validate(platform.Value, GovernedReleaseCatalogFieldLimits.PlatformDigest, "component.platformDigest");
            }
            foreach (var role in component.Roles)
                Validate(role, GovernedReleaseCatalogFieldLimits.Role, "component.role");
            foreach (var capability in component.Capabilities)
                Validate(capability, GovernedReleaseCatalogFieldLimits.Capability, "component.capability");
            foreach (var endpoint in component.Endpoints)
            {
                Validate(endpoint.Name, GovernedReleaseCatalogFieldLimits.EndpointName, "endpoint.name");
                Validate(endpoint.Protocol, GovernedReleaseCatalogFieldLimits.EndpointProtocol, "endpoint.protocol");
                Validate(endpoint.Visibility, GovernedReleaseCatalogFieldLimits.EndpointVisibility, "endpoint.visibility");
                Validate(endpoint.Path, GovernedReleaseCatalogFieldLimits.EndpointPath, "endpoint.path");
            }
        }

        foreach (var evidence in topology.Evidence)
        {
            Validate(evidence.Kind, GovernedReleaseCatalogFieldLimits.EvidenceKind, "evidence.kind");
            Validate(evidence.Reference, GovernedReleaseCatalogFieldLimits.EvidenceReference, "evidence.reference");
            Validate(evidence.Digest, GovernedReleaseCatalogFieldLimits.EvidenceDigest, "evidence.digest");
        }
    }

    private static void Validate(string? value, int maxLength, string field)
    {
        if (value is not null && value.Length > maxLength)
            throw new GovernedReleaseCatalogStorageValidationException(field);
    }
}

public sealed class GovernedReleaseCatalogStorageValidationException(string field)
    : Exception($"Projected catalog field {field} exceeds its storage length.");
