using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Core.Ownership;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class HealingCompositionTests
{
    [Fact]
    public void AddPlatformHealing_RegistersCorePersistenceAndValidatedOptions()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:DiscoveryEnabled"] = "false",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddSingleton<IHealingProviderCredentialResolver, UnusedCredentialResolver>();

        services.AddPlatformHealing(configuration, Environment("Development"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IOptions<HealingOptions>>().Value.DiscoveryEnabled.Should().BeFalse();
        provider.GetRequiredService<HealingKillSwitch>().Should().NotBeNull();
        var permissionContribution = provider.GetServices<IWorkspacePermissionContribution>().Single();
        permissionContribution.All.Should().BeEquivalentTo(HealingPermissions.All);
        permissionContribution.OwnerDefaults.Should().BeEquivalentTo(HealingPermissions.All);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<HealingDbContext>().Should().NotBeNull();
        var store = scope.ServiceProvider.GetRequiredService<HealingStore>();
        scope.ServiceProvider.GetRequiredService<IHealingAuditStore>().Should().BeSameAs(store);
        scope.ServiceProvider.GetRequiredService<HealingAuditService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IProviderConnectionValidator>()
            .Should().BeOfType<GitHubProviderConnectionValidator>();
    }

    [Fact]
    public void AddPlatformHealing_RejectsInvalidOptionsWhenResolved()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:LeaseDuration"] = "00:00:04",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddPlatformHealing(configuration, Environment("Development"));

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<HealingOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*LeaseDuration*");
    }

    [Fact]
    public void HealingKillSwitch_ObservesEmergencyConfigurationReloads()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Healing:PlatformKillSwitch"] = "false",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddPlatformHealing(configuration, Environment("Production"));
        using var provider = services.BuildServiceProvider();
        var killSwitch = provider.GetRequiredService<HealingKillSwitch>();
        var workspace = new HealingWorkspaceConfiguration();
        var application = new HealingConfiguration { DiscoveryEnabled = true };
        killSwitch.CanDiscover(workspace, application).Allowed.Should().BeTrue();

        configuration["Healing:PlatformKillSwitch"] = "true";
        ((IConfigurationRoot)configuration).Reload();

        killSwitch.CanDiscover(workspace, application).Should().Be(
            HealingGateResult.Block(HealingGateReasonCodes.PlatformKillSwitch));
    }

    [Theory]
    [InlineData(false, "Production", false)]
    [InlineData(true, "Testing", false)]
    [InlineData(true, "Production", true)]
    public void AddHostedWorker_RequiresExplicitEnablementAndNeverRunsInTests(
        bool workersEnabled,
        string environmentName,
        bool expected)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:Workers:Enabled"] = workersEnabled.ToString(),
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();

        services.AddPlatformHealing(configuration, Environment(environmentName))
            .AddHostedWorker<TestHealingWorker>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().OfType<TestHealingWorker>().Any().Should().Be(expected);
    }

    [Fact]
    public async Task MigratePlatformHealingDatabaseAsync_AppliesTheDedicatedSqliteMigrations()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-composition-{Guid.NewGuid():N}.db");
        try
        {
            var configuration = Configuration(new Dictionary<string, string?>
            {
                ["Healing:Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Healing"] = $"Data Source={databasePath}"
            });
            var services = new ServiceCollection();
            services.AddPlatformHealing(configuration, Environment("Production"));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.MigratePlatformHealingDatabaseAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
            (await dbContext.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
            (await dbContext.HealingConfigurations.CountAsync()).Should().Be(0);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MapPlatformHealingEndpoints_InvokesRegisteredModules()
    {
        var module = new TestEndpointModule();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IHealingEndpointModule>(module);
        await using var app = builder.Build();

        app.MapPlatformHealingEndpoints();

        module.WasMapped.Should().BeTrue();
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHealingWorker : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedCredentialResolver : IHealingProviderCredentialResolver
    {
        public ValueTask<string?> ResolveAsync(Guid workspaceId, string credentialReference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class TestEndpointModule : IHealingEndpointModule
    {
        public bool WasMapped { get; private set; }

        public void MapEndpoints(IEndpointRouteBuilder endpoints) => WasMapped = true;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Elsa.Platform.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
