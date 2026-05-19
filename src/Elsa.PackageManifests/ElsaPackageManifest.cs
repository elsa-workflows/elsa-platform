using Elsa.PackageManifests.Compatibility;
using Elsa.PackageManifests.Documentation;
using Elsa.PackageManifests.Licensing;

namespace Elsa.PackageManifests;

/// <summary>
/// Describes an Elsa NuGet package using a stable, versioned wire contract.
/// </summary>
public sealed class ElsaPackageManifest : ExtensibleManifestObject
{
    /// <summary>The manifest schema version, independent from the NuGet package version.</summary>
    public string SchemaVersion { get; init; } = ManifestSchemaVersions.Current;
    /// <summary>The NuGet package identity this manifest belongs to.</summary>
    public PackageIdentityManifest Package { get; init; } = new();
    /// <summary>Human-readable package name for catalog and builder experiences.</summary>
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<FeatureManifest> Features { get; init; } = [];
    public CompatibilityManifest? Compatibility { get; init; }
    public IReadOnlyList<DependencyManifest> Dependencies { get; init; } = [];
    public IReadOnlyList<ConflictManifest> Conflicts { get; init; } = [];
    public LicenseManifest? License { get; init; }
    public DocumentationManifest? Documentation { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}

/// <summary>
/// Identifies the NuGet package containing the manifest.
/// </summary>
public sealed class PackageIdentityManifest : ExtensibleManifestObject
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
}
