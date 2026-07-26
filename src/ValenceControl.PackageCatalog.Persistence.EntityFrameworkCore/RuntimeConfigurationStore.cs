using ValenceControl.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class RuntimeConfigurationStore(CatalogDbContext dbContext) : IRuntimeConfigurationStore
{
    public async Task<RuntimeConfiguration> AddAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await dbContext.RuntimeConfigurations.AddAsync(configuration, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    public async Task<IReadOnlyList<RuntimeConfiguration>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        await dbContext.RuntimeConfigurations
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.SoftDeletedAt == null)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public Task<RuntimeConfiguration?> GetAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        dbContext.RuntimeConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null, cancellationToken);

    public async Task<RuntimeConfiguration?> UpdateAsync(Guid workspaceId, Guid id, RuntimeConfigurationMutation mutation, CancellationToken cancellationToken = default)
    {
        var configuration = await dbContext.RuntimeConfigurations
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null, cancellationToken);
        if (configuration is null)
            return null;

        configuration.Name = mutation.Name;
        configuration.Description = mutation.Description;
        configuration.IntentJson = mutation.IntentJson;
        configuration.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    public async Task<bool> SoftDeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
    {
        var configuration = await dbContext.RuntimeConfigurations
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null, cancellationToken);
        if (configuration is null)
            return false;

        configuration.SoftDeletedAt = DateTimeOffset.UtcNow;
        configuration.UpdatedAt = configuration.SoftDeletedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RuntimeConfigurationVersion?> AddVersionAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
    {
        var configuration = await dbContext.RuntimeConfigurations
            .Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null, cancellationToken);
        if (configuration is null)
            return null;

        var version = new RuntimeConfigurationVersion
        {
            RuntimeConfigurationId = configuration.Id,
            VersionNumber = configuration.Versions.Count == 0 ? 1 : configuration.Versions.Max(x => x.VersionNumber) + 1,
            Name = configuration.Name,
            Description = configuration.Description,
            IntentJson = configuration.IntentJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await dbContext.RuntimeConfigurationVersions.AddAsync(version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<IReadOnlyList<RuntimeConfigurationVersion>> ListVersionsAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.RuntimeConfigurationVersions
            .AsNoTracking()
            .Where(x => x.RuntimeConfiguration != null && x.RuntimeConfiguration.WorkspaceId == workspaceId && x.RuntimeConfiguration.Id == id && x.RuntimeConfiguration.SoftDeletedAt == null)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);
}
