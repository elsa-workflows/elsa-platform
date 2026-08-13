namespace ValenceControl.Deployment.Artifacts;

public sealed record ArtifactTypeDefinition(
    string TypeId,
    string DisplayName,
    string Description,
    string OwnedBy,
    IReadOnlyList<string> SupportedSchemaVersions,
    bool Enabled,
    string? DefaultRuntimeFamily = null,
    IReadOnlyList<string>? DefaultRequiredCapabilities = null);

public interface IArtifactTypeRegistry
{
    IReadOnlyList<ArtifactTypeDefinition> ListTypes();

    ArtifactTypeDefinition? FindType(string typeId);
}
