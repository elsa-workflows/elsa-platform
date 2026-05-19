using System.Text.Json;
using Elsa.Catalog.Core.Builder;

namespace Elsa.Catalog.Core.RuntimeConfigurations;

public sealed class RuntimeConfigurationService(IRuntimeConfigurationStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<RuntimeConfiguration>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.ListAsync(workspaceId, cancellationToken);

    public Task<RuntimeConfiguration?> GetAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        store.GetAsync(workspaceId, id, cancellationToken);

    public async Task<RuntimeConfiguration> CreateAsync(Guid workspaceId, string name, string? description, RuntimeBuilderIntent intent, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await store.AddAsync(new RuntimeConfiguration
        {
            WorkspaceId = workspaceId,
            Name = NormalizeName(name),
            Description = NormalizeBlank(description),
            IntentJson = SerializeIntent(intent),
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
    }

    public Task<RuntimeConfiguration?> UpdateAsync(Guid workspaceId, Guid id, string name, string? description, RuntimeBuilderIntent intent, CancellationToken cancellationToken = default) =>
        store.UpdateAsync(workspaceId, id, new RuntimeConfigurationMutation(NormalizeName(name), NormalizeBlank(description), SerializeIntent(intent)), cancellationToken);

    public Task<bool> DeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        store.SoftDeleteAsync(workspaceId, id, cancellationToken);

    public async Task<RuntimeConfiguration?> CloneAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
    {
        var source = await store.GetAsync(workspaceId, id, cancellationToken);
        if (source is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        return await store.AddAsync(new RuntimeConfiguration
        {
            WorkspaceId = workspaceId,
            Name = $"{source.Name} Copy",
            Description = source.Description,
            IntentJson = source.IntentJson,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
    }

    public Task<RuntimeConfigurationVersion?> CreateVersionAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        store.AddVersionAsync(workspaceId, id, cancellationToken);

    public Task<IReadOnlyList<RuntimeConfigurationVersion>> ListVersionsAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        store.ListVersionsAsync(workspaceId, id, cancellationToken);

    public static RuntimeBuilderIntent DeserializeIntent(string json) =>
        JsonSerializer.Deserialize<RuntimeBuilderIntent>(json, JsonOptions)
        ?? throw new InvalidOperationException("Saved runtime configuration intent is invalid.");

    public static string SerializeIntent(RuntimeBuilderIntent intent) =>
        JsonSerializer.Serialize(intent, JsonOptions);

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Untitled Runtime" : name.Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
