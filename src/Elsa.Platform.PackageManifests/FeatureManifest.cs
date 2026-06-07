using Elsa.Platform.PackageManifests.Compatibility;
using Elsa.Platform.PackageManifests.Infrastructure;

namespace Elsa.Platform.PackageManifests;

/// <summary>
/// Describes one feature exposed by an Elsa package.
/// </summary>
public sealed class FeatureManifest : ExtensibleManifestObject
{
    /// <summary>Stable feature identifier used by catalog and builder clients.</summary>
    public string Id { get; init; } = "";
    /// <summary>Assembly-qualified or namespace-qualified CLR type name for display and diagnostics only.</summary>
    public string TypeName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    /// <summary>Legacy single category. Prefer <see cref="Categories"/> for new manifests.</summary>
    public string? Category { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public CompatibilityManifest? Compatibility { get; init; }
    public IReadOnlyList<FeatureSettingManifest> Settings { get; init; } = [];
    public IReadOnlyList<DependencyManifest> Dependencies { get; init; } = [];
    public IReadOnlyList<ConflictManifest> Conflicts { get; init; } = [];
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];
    public IReadOnlyList<InfrastructureRequirementManifest> Infrastructure { get; init; } = [];
    public bool Advanced { get; init; }
    public bool Experimental { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
