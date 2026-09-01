using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Composes the concrete Azure Bicep runner only for an explicitly enabled and
/// fully validated provider worker. The default API image remains fail-closed;
/// an enabled worker must provide its verified CLI/tools and checked-in template
/// root through the deployment host configuration.
/// </summary>
internal static class AzureProviderRunnerComposition
{
    public static void AddRunner(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var workerEnabled = configuration.GetValue<bool>("Deployment:AzureProvider:WorkerEnabled");
        var runnerSection = configuration.GetSection(AzureProviderRunnerOptions.ConfigurationSection);
        if (!workerEnabled)
        {
            services.AddScoped<IAzureProviderRunner>(_ => new UnconfiguredAzureProviderRunner());
            services.AddScoped<IAzureSecretResolver>(_ => new UnconfiguredAzureSecretResolver());
            return;
        }

        var options = runnerSection.Get<AzureProviderRunnerOptions>() ?? new();
        if (!options.Enabled)
            throw new InvalidOperationException("Azure provider worker is enabled but its concrete runner is not enabled.");
        options.Validate();

        var scopeSection = configuration.GetSection(AzureProviderTargetScope.ConfigurationSection);
        var scope = new AzureProviderTargetScope(
            scopeSection[nameof(AzureProviderTargetScope.SubscriptionId)] ?? "",
            scopeSection[nameof(AzureProviderTargetScope.ResourceGroupName)] ?? "",
            scopeSection[nameof(AzureProviderTargetScope.RegistrySubscriptionId)] ?? "",
            scopeSection[nameof(AzureProviderTargetScope.RegistryResourceGroupName)] ?? "",
            scopeSection[nameof(AzureProviderTargetScope.RegistryName)] ?? "",
            scopeSection[nameof(AzureProviderTargetScope.Location)] ?? "");
        scope.Validate();
        var secretResolver = ConfiguredAzureSecretResolver.Create(configuration);
        services.AddScoped<IAzureSecretResolver>(_ => secretResolver);
        services.AddScoped<IAzureProviderRunner>(_ => new AzureBicepProviderRunner(options, scope, secretResolver));
    }
}
