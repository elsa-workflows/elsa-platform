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
    public ResolvedElsaApplicationPlan Normalize()
    {
        var packages = ResolvedPlanNormalization.RequireItems(Packages, "packages");
        var providerCapabilities = ResolvedPlanNormalization.RequireItems(ProviderCapabilities, "providerCapabilities");
        var evidence = ResolvedPlanNormalization.RequireItems(Evidence, "evidence");

        return this with
        {
            Packages = packages.Select(x => x.Normalize()).OrderBy(x => x.SourceId).ThenBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase).ToArray(),
            Topology = Topology?.Normalize() ?? throw ResolvedPlanNormalization.Missing("topology"),
            Configuration = Configuration?.Normalize() ?? throw ResolvedPlanNormalization.Missing("configuration"),
            Capacity = Capacity?.Normalize() ?? throw ResolvedPlanNormalization.Missing("capacity"),
            Network = Network?.Normalize() ?? throw ResolvedPlanNormalization.Missing("network"),
            ProviderCapabilities = providerCapabilities.Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            Evidence = evidence.Select(x => x.Normalize()).OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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
    internal ResolvedElsaTopology Normalize()
    {
        var components = ResolvedPlanNormalization.RequireItems(Components, "topology.components");
        return this with
        {
            Components = components.Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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
    internal ResolvedElsaComponent Normalize()
    {
        var endpoints = ResolvedPlanNormalization.RequireItems(Endpoints, $"component:{Id}.endpoints");
        return this with
        {
            Roles = (Roles ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            RuntimeKinds = (RuntimeKinds ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Endpoints = endpoints.Select(x => x.Normalize()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Capabilities = (Capabilities ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Image = Image?.Normalize() ?? throw ResolvedPlanNormalization.Missing($"component:{Id}.image")
        };
    }
}

public sealed record ResolvedImageIdentity(
    string RegistryClass,
    string Repository,
    string Reference,
    string Digest,
    IReadOnlyDictionary<string, string>? PlatformDigests = null)
{
    internal ResolvedImageIdentity Normalize()
    {
        if (PlatformDigests is null)
            return this;

        var duplicate = PlatformDigests.Keys
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Platform digest key {duplicate.Key} is duplicated case-insensitively.", nameof(PlatformDigests));

        return this with
        {
            PlatformDigests = new SortedDictionary<string, string>(
                PlatformDigests.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };
    }
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
    internal ResolvedElsaPackage Normalize()
    {
        var features = ResolvedPlanNormalization.RequireItems(Features, $"package:{PackageId}.features");
        return this with
        {
            RuntimeKinds = (RuntimeKinds ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Features = features.Select(x => x.Normalize()).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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
    internal ResolvedConfigurationShape Normalize()
    {
        var entries = ResolvedPlanNormalization.RequireItems(Entries, "configuration.entries");
        return this with
        {
            Entries = entries.Select(x => x.Normalize()).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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
        SourceFeatureId = string.IsNullOrWhiteSpace(SourceFeatureId) ? null : SourceFeatureId.Trim(),
        Value = Value is { } value && value.ValueKind != JsonValueKind.Undefined
            ? ResolvedPlanJsonCanonicalizer.Canonicalize(value)
            : null
    };
}

public sealed record ResolvedCapacityOutcome(
    IReadOnlyList<ResolvedComponentCapacity> Components,
    IReadOnlyList<ResolvedStorageCapacity> Storage)
{
    internal ResolvedCapacityOutcome Normalize()
    {
        var components = ResolvedPlanNormalization.RequireItems(Components, "capacity.components");
        var storage = ResolvedPlanNormalization.RequireItems(Storage, "capacity.storage");
        return this with
        {
            Components = components.Select(x => x.Normalize()).OrderBy(x => x.ComponentId, StringComparer.OrdinalIgnoreCase).ToArray(),
            Storage = storage.Select(x => x.Normalize()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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
    internal ResolvedNetworkOutcome Normalize()
    {
        var endpoints = ResolvedPlanNormalization.RequireItems(Endpoints, "network.endpoints");
        return this with
        {
            AllowedOutboundDestinations = (AllowedOutboundDestinations ?? []).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Endpoints = endpoints.Select(x => x.Normalize()).OrderBy(x => x.ComponentId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
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

internal static class ResolvedPlanNormalization
{
    public static IReadOnlyList<T> RequireItems<T>(IReadOnlyList<T>? items, string path) where T : class
    {
        if (items is null)
            throw Missing(path);
        if (items.Any(x => x is null))
            throw new ArgumentException($"Collection {path} contains a null item.", path);
        return items;
    }

    public static ArgumentException Missing(string path) => new($"Resolved application plan field {path} is required.", path);
}
