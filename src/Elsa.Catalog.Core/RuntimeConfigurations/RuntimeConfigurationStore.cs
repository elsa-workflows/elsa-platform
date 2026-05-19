namespace Elsa.Catalog.Core.RuntimeConfigurations;

public interface IRuntimeConfigurationStore
{
    Task<RuntimeConfiguration> AddAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeConfiguration>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<RuntimeConfiguration?> GetAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default);
    Task<RuntimeConfiguration?> UpdateAsync(Guid workspaceId, Guid id, RuntimeConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default);
    Task<RuntimeConfigurationVersion?> AddVersionAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeConfigurationVersion>> ListVersionsAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default);
}
