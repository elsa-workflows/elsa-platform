using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Core.Workspace;

/// <summary>
/// A desired-state record together with the typed reconciliation identity used by
/// analytical consumers. The payload is retained as a cloned JSON element only
/// for existing safe display/validation projections; it is never sent to a
/// runtime command by this reader.
/// </summary>
public sealed record DesiredStateResource(
    string Kind,
    string Name,
    DeploymentResource Resource,
    JsonElement Payload,
    JsonElement ArtifactPayload)
{
    public string Key => $"{Kind}:{Name}";

    public string PayloadJson => Payload.GetRawText();

    public DesiredStateRecordKind? KnownKind =>
        Enum.TryParse<DesiredStateRecordKind>(Kind, ignoreCase: true, out var kind) ? kind : null;

    public bool IsKind(DesiredStateRecordKind kind) =>
        string.Equals(Kind, kind.ToString(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the structured desired-state envelope once into the shared typed
/// resource model used by promotion and deployability planning.
/// </summary>
public static class DesiredStateResourceReader
{
    private static readonly JsonElement EmptyPayload = JsonSerializer.SerializeToElement(new object());

    public static IReadOnlyList<DesiredStateResource> Read(string desiredStateJson)
    {
        try
        {
            using var document = JsonDocument.Parse(desiredStateJson);
            var records = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("records", out var recordsElement)
                && recordsElement.ValueKind == JsonValueKind.Array
                    ? recordsElement
                    : document.RootElement;
            if (records.ValueKind != JsonValueKind.Array)
                return [];

            return records.EnumerateArray()
                .Select(ReadRecord)
                .Where(record => record is not null)
                .Cast<DesiredStateResource>()
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DesiredStateResource? ReadRecord(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(kindElement.GetString())
            || !record.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
            return null;

        var kind = kindElement.GetString()!;
        var name = nameElement.GetString()!;
        var hasPayload = record.TryGetProperty("payload", out var payloadElement);
        var payload = hasPayload ? payloadElement.Clone() : EmptyPayload;
        var artifactPayload = hasPayload && payloadElement.ValueKind == JsonValueKind.Object
            ? payload
            : record.Clone();
        var resource = new DeploymentResource(
            new DeploymentResourceId(kind, name),
            desiredStateHash: new ArtifactDigest(
                "sha256",
                WorkspaceDeploymentService.ComputeDesiredStateHash(payload)));

        return new DesiredStateResource(kind, name, resource, payload, artifactPayload);
    }
}
