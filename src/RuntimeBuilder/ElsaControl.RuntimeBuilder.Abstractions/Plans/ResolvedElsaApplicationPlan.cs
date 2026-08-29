using System.Text.Json;

namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// The contract between Elsa Control's catalog/desired-state resolver and a deployment provider.
/// It intentionally contains service outcomes and immutable identities, not provider resources.
/// </summary>
public sealed record ResolvedElsaApplicationPlan(
    string SchemaVersion,
    ResolvedElsaRelease Release,
    ResolvedElsaTopology Topology,
    IReadOnlyList<ResolvedElsaPackage> Packages,
    ResolvedConfigurationShape Configuration,
    ResolvedCapacityOutcome Capacity,
    ResolvedNetworkOutcome Network,
    string Isolation,
    ResolvedReleasePolicy ReleasePolicy,
    IReadOnlyList<ProviderCapabilityRequirement> ProviderCapabilities,
    IReadOnlyList<ResolvedPlanEvidence> Evidence)
{
    /// <summary>
    /// Returns a copy with unordered collections normalized for stable serialization and hashing.
    /// </summary>
    public ResolvedElsaApplicationPlan Normalize() => this with
    {
        Packages = (Packages ?? []).Select(x => x.Normalize()).OrderBy(x => x.SourceId).ThenBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase).ToArray(),
        Topology = Topology.Normalize(),
        Configuration = Configuration.Normalize(),
        Capacity = Capacity.Normalize(),
        Network = Network.Normalize(),
        ProviderCapabilities = (ProviderCapabilities ?? []).Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
        Evidence = (Evidence ?? []).Select(x => x.Normalize()).OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public static class ResolvedElsaApplicationPlanSchema
{
    public const string CurrentVersion = "1";
}

/// <summary>
/// Producer-owned exact release identity. Release line and version are data, never an Elsa-major enum.
/// </summary>
public sealed record ResolvedElsaRelease(
    string DistributionId,
    string ReleaseLine,
    string Version,
    string SourceRepository,
    string SourceCommit,
    string ReleaseManifestReference,
    string ReleaseManifestDigest);

/// <summary>
/// A topology is a composition of independently addressable runtime components.
/// Combined is therefore one possible composition, not a special schema case.
/// </summary>
public sealed record ResolvedElsaTopology(
    string Id,
    IReadOnlyList<ResolvedElsaComponent> Components)
{
    internal ResolvedElsaTopology Normalize() => this with
    {
        Components = (Components ?? []).Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedElsaComponent(
    string Id,
    IReadOnlyList<string> Roles,
    ResolvedImageIdentity Image,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<ResolvedElsaEndpoint> Endpoints,
    IReadOnlyList<string> Capabilities,
    string? CompanionComponentId = null)
{
    internal ResolvedElsaComponent Normalize() => this with
    {
        Roles = (Roles ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        RuntimeKinds = (RuntimeKinds ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Endpoints = (Endpoints ?? []).Select(x => x.Normalize()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
        Capabilities = (Capabilities ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Image = Image.Normalize()
    };
}

public sealed record ResolvedImageIdentity(
    string RegistryClass,
    string Repository,
    string Reference,
    string Digest,
    IReadOnlyDictionary<string, string>? PlatformDigests = null)
{
    internal ResolvedImageIdentity Normalize() => this with
    {
        PlatformDigests = PlatformDigests is null
            ? null
            : new SortedDictionary<string, string>(
                PlatformDigests.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
    };
}

public sealed record ResolvedElsaEndpoint(
    string Name,
    string Protocol,
    int Port,
    string Visibility,
    bool RequiresTls,
    string? Path = null)
{
    internal ResolvedElsaEndpoint Normalize() => this with
    {
        Path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim()
    };
}

public sealed record ResolvedElsaPackage(
    Guid SourceId,
    string PackageId,
    string Version,
    string ManifestDigest,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<ResolvedElsaFeature> Features)
{
    internal ResolvedElsaPackage Normalize() => this with
    {
        RuntimeKinds = (RuntimeKinds ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Features = (Features ?? []).Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedElsaFeature(
    string Id,
    string? TypeName,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<string> RequiredCapabilities)
{
    internal ResolvedElsaFeature Normalize() => this with
    {
        RuntimeKinds = (RuntimeKinds ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        RequiredCapabilities = (RequiredCapabilities ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

/// <summary>
/// Resolved configuration contains shape plus non-secret values or provider-backed references.
/// A secret value is never allowed in this contract.
/// </summary>
public sealed record ResolvedConfigurationShape(
    IReadOnlyList<ResolvedConfigurationEntry> Entries)
{
    internal ResolvedConfigurationShape Normalize() => this with
    {
        Entries = (Entries ?? []).Select(x => x.Normalize()).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedConfigurationEntry(
    string Key,
    string JsonType,
    bool Required,
    bool Secret,
    bool RestartRequired,
    string? EnvironmentVariable,
    JsonElement? Value,
    string? SecretReference,
    string? SourceFeatureId)
{
    internal ResolvedConfigurationEntry Normalize() => this with
    {
        EnvironmentVariable = string.IsNullOrWhiteSpace(EnvironmentVariable) ? null : EnvironmentVariable.Trim(),
        SecretReference = string.IsNullOrWhiteSpace(SecretReference) ? null : SecretReference.Trim(),
        SourceFeatureId = string.IsNullOrWhiteSpace(SourceFeatureId) ? null : SourceFeatureId.Trim()
    };
}

public sealed record ResolvedCapacityOutcome(
    IReadOnlyList<ResolvedComponentCapacity> Components,
    IReadOnlyList<ResolvedStorageCapacity> Storage)
{
    internal ResolvedCapacityOutcome Normalize() => this with
    {
        Components = (Components ?? []).Select(x => x.Normalize()).OrderBy(x => x.ComponentId, StringComparer.OrdinalIgnoreCase).ToArray(),
        Storage = (Storage ?? []).Select(x => x.Normalize()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedComponentCapacity(
    string ComponentId,
    int MinReplicas,
    int MaxReplicas,
    int CpuMillicores,
    int MemoryMiB,
    int? EphemeralStorageMiB = null)
{
    internal ResolvedComponentCapacity Normalize() => this;
}

public sealed record ResolvedStorageCapacity(
    string Name,
    string Kind,
    string Durability,
    string AccessMode,
    int? SizeGiB = null)
{
    internal ResolvedStorageCapacity Normalize() => this;
}

public sealed record ResolvedNetworkOutcome(
    string Ingress,
    string Egress,
    bool RequiresPrivateConnectivity,
    IReadOnlyList<string> AllowedOutboundDestinations,
    IReadOnlyList<ResolvedNetworkEndpoint> Endpoints)
{
    internal ResolvedNetworkOutcome Normalize() => this with
    {
        AllowedOutboundDestinations = (AllowedOutboundDestinations ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Endpoints = (Endpoints ?? []).Select(x => x.Normalize()).OrderBy(x => x.ComponentId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedNetworkEndpoint(
    string ComponentId,
    string Name,
    string Protocol,
    int Port,
    string Visibility,
    bool RequiresTls,
    string? Path = null)
{
    internal ResolvedNetworkEndpoint Normalize() => this with
    {
        Path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim()
    };
}

/// <summary>
/// Policy is separate from the exact release identity. The three transition values make
/// patch rollout, minor upgrade and major migration distinguishable to every provider.
/// </summary>
public sealed record ResolvedReleasePolicy(
    string Channel,
    string Lifecycle,
    string RolloutRing,
    string PatchUpdates,
    string MinorUpdates,
    string MajorMigrations);

public sealed record ProviderCapabilityRequirement(
    string Id,
    string Description,
    bool Required,
    IReadOnlyList<string> Parameters)
{
    internal ProviderCapabilityRequirement Normalize() => this with
    {
        Parameters = (Parameters ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray()
    };
}

public sealed record ResolvedPlanEvidence(
    string Kind,
    string Reference,
    string? Digest,
    string Description)
{
    internal ResolvedPlanEvidence Normalize() => this with
    {
        Digest = string.IsNullOrWhiteSpace(Digest) ? null : Digest.Trim()
    };
}
