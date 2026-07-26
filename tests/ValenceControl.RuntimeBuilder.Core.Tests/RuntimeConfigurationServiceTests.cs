using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using ValenceControl.RuntimeBuilder.Core.RuntimeConfigurations;
using FluentAssertions;

namespace ValenceControl.RuntimeBuilder.Core.Tests;

public sealed class RuntimeConfigurationServiceTests
{
    [Fact]
    public async Task Creates_updates_clones_and_versions_runtime_configurations()
    {
        var workspaceId = Guid.NewGuid();
        var service = new RuntimeConfigurationService(new InMemoryStore());
        var intent = MinimalIntent();

        var created = await service.CreateAsync(workspaceId, " Production ", "Initial", intent);
        var updated = await service.UpdateAsync(workspaceId, created.Id, "Production v2", null, intent);
        var clone = await service.CloneAsync(workspaceId, created.Id);
        var version = await service.CreateVersionAsync(workspaceId, created.Id);
        var versions = await service.ListVersionsAsync(workspaceId, created.Id);

        created.Id.Should().NotBeEmpty();
        updated!.Name.Should().Be("Production v2");
        clone!.Id.Should().NotBe(created.Id);
        clone.Name.Should().Be("Production v2 Copy");
        version!.VersionNumber.Should().Be(1);
        versions.Should().ContainSingle(x => x.Id == version.Id);
    }

    [Fact]
    public async Task Delete_hides_configuration_from_lists()
    {
        var workspaceId = Guid.NewGuid();
        var service = new RuntimeConfigurationService(new InMemoryStore());
        var created = await service.CreateAsync(workspaceId, "Runtime", null, MinimalIntent());

        (await service.DeleteAsync(workspaceId, created.Id)).Should().BeTrue();

        (await service.ListAsync(workspaceId)).Should().BeEmpty();
        (await service.GetAsync(workspaceId, created.Id)).Should().BeNull();
    }

    private static RuntimeBuilderIntent MinimalIntent() =>
        new(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages"));

    private sealed class InMemoryStore : IRuntimeConfigurationStore
    {
        private readonly List<RuntimeConfiguration> configurations = [];

        public Task<RuntimeConfiguration> AddAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
        {
            configurations.Add(configuration);
            return Task.FromResult(configuration);
        }

        public Task<IReadOnlyList<RuntimeConfiguration>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RuntimeConfiguration>>(configurations.Where(x => x.WorkspaceId == workspaceId && x.SoftDeletedAt == null).ToList());

        public Task<RuntimeConfiguration?> GetAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(configurations.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null));

        public Task<RuntimeConfiguration?> UpdateAsync(Guid workspaceId, Guid id, RuntimeConfigurationMutation mutation, CancellationToken cancellationToken = default)
        {
            var configuration = configurations.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null);
            if (configuration is not null)
            {
                configuration.Name = mutation.Name;
                configuration.Description = mutation.Description;
                configuration.IntentJson = mutation.IntentJson;
                configuration.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Task.FromResult(configuration);
        }

        public Task<bool> SoftDeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
        {
            var configuration = configurations.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null);
            if (configuration is null)
                return Task.FromResult(false);
            configuration.SoftDeletedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task<RuntimeConfigurationVersion?> AddVersionAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
        {
            var configuration = configurations.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null);
            if (configuration is null)
                return Task.FromResult<RuntimeConfigurationVersion?>(null);

            var version = new RuntimeConfigurationVersion
            {
                RuntimeConfigurationId = configuration.Id,
                VersionNumber = configuration.Versions.Count + 1,
                Name = configuration.Name,
                Description = configuration.Description,
                IntentJson = configuration.IntentJson
            };
            configuration.Versions.Add(version);
            return Task.FromResult<RuntimeConfigurationVersion?>(version);
        }

        public Task<IReadOnlyList<RuntimeConfigurationVersion>> ListVersionsAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
        {
            var configuration = configurations.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == id && x.SoftDeletedAt == null);
            return Task.FromResult<IReadOnlyList<RuntimeConfigurationVersion>>(configuration?.Versions.ToList() ?? []);
        }
    }
}
