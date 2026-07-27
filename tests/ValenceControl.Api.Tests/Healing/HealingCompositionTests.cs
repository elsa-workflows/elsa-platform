using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Api.Workspace.Healing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingCompositionTests
{
    [Fact]
    public void AddControlHealing_RegistersCorePersistenceAndValidatedOptions()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:DiscoveryEnabled"] = "false",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddSingleton<IHealingProviderCredentialResolver, UnusedCredentialResolver>();

        services.AddControlHealing(configuration, Environment("Development"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.False(provider.GetRequiredService<IOptions<HealingOptions>>().Value.DiscoveryEnabled);
        Assert.NotNull(provider.GetRequiredService<HealingKillSwitch>());
        var permissionContribution = provider.GetServices<IWorkspacePermissionContribution>().Single();
        Assert.Equivalent(HealingPermissions.All, permissionContribution.All);
        Assert.Equivalent(HealingPermissions.All, permissionContribution.OwnerDefaults);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HealingDbContext>());
        var store = scope.ServiceProvider.GetRequiredService<HealingStore>();
        Assert.Same(store, scope.ServiceProvider.GetRequiredService<IHealingAuditStore>());
        Assert.IsType<HealingMergeEvaluationStore>(scope.ServiceProvider.GetRequiredService<IHealingMergeEvaluationStore>());
        Assert.IsType<HealingHumanProviderCommandStore>(scope.ServiceProvider.GetRequiredService<IHumanProviderCommandStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HealingAuditService>());
        Assert.IsType<GitHubProviderConnectionValidator>(scope.ServiceProvider.GetRequiredService<IProviderConnectionValidator>());
    }

    [Fact]
    public void AddControlHealing_RejectsInvalidOptionsWhenResolved()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:LeaseDuration"] = "00:00:04",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddControlHealing(configuration, Environment("Development"));

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<HealingOptions>>().Value;

        var exception = Assert.Throws<OptionsValidationException>(resolve);
        Assert.Contains("LeaseDuration", exception.Message);
    }

    [Fact]
    public void HealingKillSwitch_ObservesEmergencyConfigurationReloads()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Healing:ControlKillSwitch"] = "false",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddControlHealing(configuration, Environment("Production"));
        using var provider = services.BuildServiceProvider();
        var killSwitch = provider.GetRequiredService<HealingKillSwitch>();
        var workspace = new HealingWorkspaceConfiguration();
        var application = new HealingConfiguration { DiscoveryEnabled = true };
        Assert.True(killSwitch.CanDiscover(workspace, application).Allowed);

        configuration["Healing:ControlKillSwitch"] = "true";
        ((IConfigurationRoot)configuration).Reload();

        Assert.Equal(
            HealingGateResult.Block(HealingGateReasonCodes.ControlKillSwitch),
            killSwitch.CanDiscover(workspace, application));
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

        services.AddControlHealing(configuration, Environment(environmentName))
            .AddHostedWorker<TestHealingWorker>();

        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetServices<IHostedService>().OfType<TestHealingWorker>().Any());
    }

    [Theory]
    [InlineData("Healing:IncidentReviewEnabled", false)]
    [InlineData("Healing:IncidentReviewEnabled", true)]
    [InlineData("Healing:VerificationEnabled", false)]
    [InlineData("Healing:VerificationEnabled", true)]
    public void StageControlledRegistration_TracksItsOwnFlag(string configurationKey, bool enabled)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:Workers:Enabled"] = "true",
            [configurationKey] = enabled.ToString(),
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = services.AddControlHealing(configuration, Environment("Production"));

        if (configurationKey.EndsWith(nameof(HealingOptions.IncidentReviewEnabled), StringComparison.Ordinal))
        {
            builder.AddHostedWorker<HealingSignalInboxHostedService>(HealingOptions.IncidentReviewEnabledConfigurationKey)
                .AddEndpointModule<WorkspaceHealingIncidentEndpointModule>(HealingOptions.IncidentReviewEnabledConfigurationKey);
        }
        else
        {
            builder.AddHostedWorker<HealingVerificationHostedService>(HealingOptions.VerificationEnabledConfigurationKey)
                .AddEndpointModule<HealingVerificationEndpointModule>(HealingOptions.VerificationEnabledConfigurationKey);
        }

        using var provider = services.BuildServiceProvider();
        if (configurationKey.EndsWith(nameof(HealingOptions.IncidentReviewEnabled), StringComparison.Ordinal))
        {
            Assert.Equal(enabled, provider.GetServices<IHostedService>().OfType<HealingSignalInboxHostedService>().Any());
            Assert.Equal(enabled, provider.GetServices<IHealingEndpointModule>().OfType<WorkspaceHealingIncidentEndpointModule>().Any());
        }
        else
        {
            Assert.Equal(enabled, provider.GetServices<IHostedService>().OfType<HealingVerificationHostedService>().Any());
            Assert.Equal(enabled, provider.GetServices<IHealingEndpointModule>().OfType<HealingVerificationEndpointModule>().Any());
        }
    }

    [Fact]
    public void Verification_failure_delivery_registers_the_production_worker_and_preserves_a_custom_consumer()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:Workers:Enabled"] = "true",
            ["Healing:VerificationEnabled"] = "true",
            ["Healing:VerificationFailureDelivery:Enabled"] = "true",
            ["Healing:VerificationFailureDelivery:Endpoint"] = "https://deployment.example.test/healing/verification-failures",
            ["Healing:VerificationFailureDelivery:SharedSecret"] = new string('s', 32),
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddScoped<IRepairVerificationFailureConsumer, TestVerificationFailureConsumer>();

        services.AddControlHealing(configuration, Environment("Production"))
            .AddHostedWorker<HealingVerificationFailureDeliveryHostedService>(
                HealingOptions.VerificationEnabledConfigurationKey);

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IHostedService>(), x => x is HealingVerificationFailureDeliveryHostedService);
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IRepairVerificationFailureConsumer>(), x => x is TestVerificationFailureConsumer);
    }

    [Fact]
    public void Verification_failure_delivery_registers_the_http_consumer_when_configured()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:VerificationFailureDelivery:Enabled"] = "true",
            ["Healing:VerificationFailureDelivery:Endpoint"] = "https://deployment.example.test/healing/verification-failures",
            ["Healing:VerificationFailureDelivery:SharedSecret"] = new string('s', 32),
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();

        services.AddControlHealing(configuration, Environment("Production"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IRepairVerificationFailureConsumer>(), x => x is HttpRepairVerificationFailureConsumer);
    }

    [Fact]
    public void Enabled_verification_failure_delivery_rejects_configuration_without_an_https_endpoint_and_strong_secret()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Healing:VerificationFailureDelivery:Enabled"] = "true",
            ["ConnectionStrings:Healing"] = "Data Source=:memory:"
        });
        var services = new ServiceCollection();

        services.AddControlHealing(configuration, Environment("Production"));

        using var provider = services.BuildServiceProvider();
        var resolveOptions = () => provider.GetRequiredService<IOptions<HealingVerificationFailureDeliveryOptions>>().Value;
        Assert.Throws<OptionsValidationException>(resolveOptions);
    }

    [Fact]
    public async Task MigrateControlHealingDatabaseAsync_AppliesTheDedicatedSqliteMigrations()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-healing-composition-{Guid.NewGuid():N}.db");
        try
        {
            var configuration = Configuration(new Dictionary<string, string?>
            {
                ["Healing:Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Healing"] = $"Data Source={databasePath}"
            });
            var services = new ServiceCollection();
            services.AddControlHealing(configuration, Environment("Production"));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.MigrateControlHealingDatabaseAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
            Assert.NotEmpty((await dbContext.Database.GetAppliedMigrationsAsync()));
            Assert.Equal(0, (await dbContext.HealingConfigurations.CountAsync()));
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MapControlHealingEndpoints_InvokesRegisteredModules()
    {
        var module = new TestEndpointModule();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IHealingEndpointModule>(module);
        await using var app = builder.Build();

        app.MapControlHealingEndpoints();

        Assert.True(module.WasMapped);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHealingWorker : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestVerificationFailureConsumer : IRepairVerificationFailureConsumer
    {
        public ValueTask<bool> ConsumeAsync(
            RepairVerificationFailedSignalLease delivery,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
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
        public string ApplicationName { get; set; } = "ValenceControl.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
