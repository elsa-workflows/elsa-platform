using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Incidents;
using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.OpenTelemetry;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.OpenTelemetry;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

public static class HealingServiceCollectionExtensions
{
    public const string WorkersEnabledConfigurationKey = "Healing:Workers:Enabled";
    public const string TestingEnvironmentName = "Testing";

    public static PlatformHealingBuilder AddPlatformHealing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<HealingOptions>()
            .Bind(configuration.GetSection(HealingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<HealingOptions>, HealingOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IWorkspacePermissionContribution, HealingWorkspacePermissionContribution>());

        services.AddHealingDbContext(configuration);
        services.TryAddScoped<HealingStore>();
        services.TryAddScoped<IHealingOwnershipStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingAdministrationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingAuditStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingIncidentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingSignalInboxStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingTelemetrySourceStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<HealingAuditService>();
        services.TryAddScoped<HealingConfigurationService>();
        services.TryAddScoped<ComponentManifestService>();
        services.TryAddScoped<IComponentManifestAttestationAuthority, PlatformManagedComponentManifestAttestationAuthority>();
        services.TryAddScoped<SourceOwnershipService>();
        services.TryAddScoped<ComponentAttributionService>();
        services.TryAddScoped<HealingIncidentService>();
        services.TryAddScoped<HealingSignalInboxWorker>();
        services.TryAddSingleton<HealingSignalNormalizer>();
        services.TryAddSingleton<HealingSignalClassifier>();
        services.TryAddSingleton<HealingFingerprintService>();
        services.TryAddScoped<IHealingSignalInboxAppender, PlatformHealingSignalInboxAppender>();
        services.TryAddScoped<IHealingTelemetryScopeResolver, AuthenticatedClaimHealingTelemetryScopeResolver>();
        services.TryAddSingleton<HealingTelemetrySourceTokenService>();
        services.TryAddScoped<HealingTelemetrySourceService>();
        services.Replace(ServiceDescriptor.Scoped<IOtlpRequestAuthenticator, PlatformHealingOtlpRequestAuthenticator>());
        services.AddOpenTelemetryDiagnosticsServices(options =>
            configuration.GetSection("Healing:OpenTelemetry").Bind(options));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOpenTelemetryIngestionContributor, HealingOpenTelemetryIngestionContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealingEndpointModule, WorkspaceHealingTelemetrySourceEndpointModule>());
        services.TryAddScoped<HealingAdministrationService>();
        services.TryAddScoped<IHealingProviderCredentialResolver, WorkspaceHealingProviderCredentialResolver>();
        services.AddHttpClient<GitHubAppTokenProvider>(client => client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<IProviderConnectionValidator, GitHubProviderConnectionValidator>(
            client => client.BaseAddress = new Uri("https://api.github.com/"));
        services.TryAddSingleton(serviceProvider =>
            new HealingKillSwitch(serviceProvider.GetRequiredService<IOptionsMonitor<HealingOptions>>()));

        return new PlatformHealingBuilder(services, configuration, environment);
    }

    public static IEndpointRouteBuilder MapPlatformHealingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in endpoints.ServiceProvider.GetServices<IHealingEndpointModule>())
            module.MapEndpoints(endpoints);

        return endpoints;
    }

    public static Task MigratePlatformHealingDatabaseAsync(
        this IServiceProvider scopedServices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);
        return scopedServices.GetRequiredService<HealingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

public sealed class PlatformHealingBuilder
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    internal PlatformHealingBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        Services = services;
        _configuration = configuration;
        _environment = environment;
    }

    public IServiceCollection Services { get; }

    public PlatformHealingBuilder AddHostedWorker<TWorker>() where TWorker : class, IHostedService
    {
        if (!_environment.IsEnvironment(HealingServiceCollectionExtensions.TestingEnvironmentName) &&
            _configuration.GetValue(HealingServiceCollectionExtensions.WorkersEnabledConfigurationKey, false))
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TWorker>());
        }

        return this;
    }

    public PlatformHealingBuilder AddEndpointModule<TModule>() where TModule : class, IHealingEndpointModule
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealingEndpointModule, TModule>());
        return this;
    }
}

public interface IHealingEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

internal sealed class HealingWorkspacePermissionContribution : IWorkspacePermissionContribution
{
    public IReadOnlySet<string> All => HealingPermissions.All;
    public IReadOnlySet<string> OwnerDefaults => HealingPermissions.All;
}
