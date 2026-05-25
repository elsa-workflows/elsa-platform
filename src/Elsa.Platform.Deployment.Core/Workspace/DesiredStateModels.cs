using System.Text.Json;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed record StructuredDesiredStateRecord(
    Guid Id,
    Guid WorkspaceId,
    Guid RevisionId,
    DesiredStateRecordKind Kind,
    string Name,
    JsonDocument Payload,
    string ContentHash);

public sealed record DesiredStateRecordInput(
    DesiredStateRecordKind Kind,
    string Name,
    JsonDocument Payload);

public enum DesiredStateRecordKind
{
    Workflow,
    Feature,
    ShellConfiguration,
    RuntimeConfiguration,
    SecretReference,
    ObservabilityBinding,
    EngineBinding
}
