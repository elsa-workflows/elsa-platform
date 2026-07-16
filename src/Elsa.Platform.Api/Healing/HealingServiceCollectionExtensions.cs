using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
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

        services.AddHealingDbContext(configuration);
        services.TryAddScoped<HealingStore>();
        services.TryAddScoped<IHealingAuditStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<HealingAuditService>();
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
