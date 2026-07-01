namespace Elsa.Platform.Deployment.Artifacts;

public sealed class ArtifactTypeRegistry : IArtifactTypeRegistry
{
    private static readonly ArtifactTypeDefinition WorkflowDefinition = new(
        ArtifactTypeIds.ElsaWorkflowDefinition,
        "Elsa Workflow Definition",
        "Workflow definition artifact submitted to Platform for promotion and runtime application.",
        "platform",
        [ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion],
        Enabled: true,
        DefaultRuntimeFamily: "elsa-workflows",
        DefaultRequiredCapabilities: [ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition)]);

    private static readonly ArtifactTypeDefinition LoomRecipe = new(
        ArtifactTypeIds.ElsaLoomRecipe,
        "Elsa Loom Recipe",
        "Loom recipe artifact submitted to Platform for promotion and runtime application.",
        "platform",
        [ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion],
        Enabled: true,
        DefaultRuntimeFamily: "elsa-workflows",
        DefaultRequiredCapabilities: [ArtifactApplyCapability.For(ArtifactTypeIds.ElsaLoomRecipe)]);

    private readonly IReadOnlyDictionary<string, ArtifactTypeDefinition> _types =
        new Dictionary<string, ArtifactTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkflowDefinition.TypeId] = WorkflowDefinition,
            [LoomRecipe.TypeId] = LoomRecipe
        };

    public IReadOnlyList<ArtifactTypeDefinition> ListTypes() =>
        _types.Values.OrderBy(x => x.TypeId, StringComparer.OrdinalIgnoreCase).ToList();

    public ArtifactTypeDefinition? FindType(string typeId) =>
        _types.TryGetValue(typeId, out var type) ? type : null;
}

public static class ArtifactApplyCapability
{
    public static string For(string artifactTypeId) => $"artifact.{artifactTypeId}.apply";

    public static string Normalize(string artifactTypeId, string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return capability;

        var trimmed = capability.Trim();
        if (trimmed.StartsWith("artifact.", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (string.Equals(artifactTypeId, ArtifactTypeIds.ElsaWorkflowDefinition, StringComparison.OrdinalIgnoreCase)
            && string.Equals(trimmed, "workflow-definition.apply", StringComparison.OrdinalIgnoreCase))
            return For(ArtifactTypeIds.ElsaWorkflowDefinition);

        if (string.Equals(artifactTypeId, ArtifactTypeIds.ElsaLoomRecipe, StringComparison.OrdinalIgnoreCase)
            && string.Equals(trimmed, "loom.recipe.apply", StringComparison.OrdinalIgnoreCase))
            return For(ArtifactTypeIds.ElsaLoomRecipe);

        return trimmed;
    }
}
