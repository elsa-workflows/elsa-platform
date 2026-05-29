using System.Text.Json;

namespace Elsa.Platform.Studio.Submit;

public sealed record WorkflowSubmissionSnapshot(
    string WorkflowDefinitionId,
    string? WorkflowDefinitionVersionId,
    string Name,
    string? Version,
    string? Description,
    string WorkflowDefinitionJson,
    string WorkflowSchemaVersion,
    string? SourceReference,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations)
{
    public static WorkflowSubmissionSnapshot FromJson(
        string workflowDefinitionId,
        string name,
        JsonDocument definition,
        string workflowSchemaVersion,
        string? workflowDefinitionVersionId = null,
        string? version = null,
        string? description = null,
        string? sourceReference = null,
        IReadOnlyDictionary<string, string>? labels = null,
        IReadOnlyDictionary<string, string>? annotations = null) =>
        new(
            workflowDefinitionId,
            workflowDefinitionVersionId,
            name,
            version,
            description,
            JsonSerializer.Serialize(definition.RootElement),
            workflowSchemaVersion,
            sourceReference,
            labels ?? new Dictionary<string, string>(),
            annotations ?? new Dictionary<string, string>());
}
