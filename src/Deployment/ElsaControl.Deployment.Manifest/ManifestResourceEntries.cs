using System.Text.Json.Nodes;

namespace ElsaControl.Deployment.Manifest;

public sealed record ManifestResources
{
    public IReadOnlyList<WorkflowManifestEntry> Workflows { get; init; } = [];

    public IReadOnlyList<VariableManifestEntry> Variables { get; init; } = [];

    public IReadOnlyList<FeatureManifestEntry> Features { get; init; } = [];

    public IReadOnlyList<PackageManifestEntry> Packages { get; init; } = [];

    public IReadOnlyList<RecipeManifestEntry> Recipes { get; init; } = [];

    public IReadOnlyDictionary<string, JsonNode?> Extensions { get; init; } = ManifestEmpty.JsonNodeDictionary;
}

public sealed record WorkflowManifestEntry
{
    public string? Id { get; init; }

    public string? Path { get; init; }

    public string? Activation { get; init; }

    public string? Version { get; init; }

    public IReadOnlyList<ManifestResourceReference> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ManifestEmpty.StringDictionary;
}

public sealed record VariableManifestEntry
{
    public string? Key { get; init; }

    public JsonNode? Value { get; init; }

    public string? Scope { get; init; }

    public IReadOnlyList<ManifestResourceReference> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ManifestEmpty.StringDictionary;
}

public sealed record FeatureManifestEntry
{
    public string? Id { get; init; }

    public string? State { get; init; }

    public IReadOnlyList<ManifestResourceReference> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ManifestEmpty.StringDictionary;
}

public sealed record PackageManifestEntry
{
    public string? Id { get; init; }

    public string? Version { get; init; }

    public IReadOnlyList<ManifestResourceReference> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ManifestEmpty.StringDictionary;
}

public sealed record RecipeManifestEntry
{
    public string? Id { get; init; }

    public string? Path { get; init; }

    public string? Version { get; init; }

    public IReadOnlyList<ManifestResourceReference> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ManifestEmpty.StringDictionary;
}

public sealed record ManifestResourceReference
{
    public string? Type { get; init; }

    public string? Id { get; init; }

    public string? Scope { get; init; }
}
