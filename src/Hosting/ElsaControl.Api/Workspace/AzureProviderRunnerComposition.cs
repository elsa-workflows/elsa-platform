using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Workspace;

internal sealed record AzureProviderRunnerAuthority(
    AzureProviderRunnerOptions Options,
    AzureProviderTargetScope Scope)
{
    public string TemplateFingerprint => Options.ComputeTemplateAuthorityFingerprint();
    public string ProviderScopeFingerprint => Options.ComputeProviderScopeFingerprint(Scope);
}

/// <summary>
/// Composes the concrete Azure Bicep runner only for an explicitly enabled and
/// fully validated provider worker. The default API image remains fail-closed;
/// an enabled worker must provide its verified CLI/tools and checked-in template
/// root through the deployment host configuration.
/// </summary>
internal static class AzureProviderRunnerComposition
{
    public static AzureProviderRunnerAuthority? AddRunner(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var workerEnabled = configuration.GetValue<bool>("Deployment:AzureProvider:WorkerEnabled");
        var runnerSection = configuration.GetSection(AzureProviderRunnerOptions.ConfigurationSection);
        if (!workerEnabled)
        {
            services.AddScoped<IAzureProviderRunner>(_ => new UnconfiguredAzureProviderRunner());
            services.AddScoped<IAzureSecretResolver>(_ => new UnconfiguredAzureSecretResolver());
            return null;
        }

        if (configuration.GetSection("Deployment:AzureProvider:Secrets").GetChildren()
            .Any(child => child["Value"] is not null))
            throw new InvalidOperationException(
                "Azure provider worker configuration must not contain raw secret values.");

        if (runnerSection.GetValue<bool>("DisposableProofMode"))
            throw new InvalidOperationException(
                "The production Azure provider worker must not use disposable proof mode.");

        var secretSection = configuration.GetSection("Deployment:AzureProvider:Secrets");
        if (secretSection.GetChildren().Any())
        {
            // External secrets require immutable, versioned Key Vault locators, which are
            // projected to canonical provider-neutral secret:// plan references. Admin and
            // signing credentials are generated per instance; only the exact provider-owned
            // instructions are accepted for those production slots.
            _ = ConfiguredAzureSecretResolver.ReadNamedReferences(
                configuration,
                requireProviderOwnedCredentials: true);
        }

        var options = runnerSection.Get<AzureProviderRunnerOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(options.AzureCliClientId))
            options = options with { AzureCliClientId = configuration["AZURE_CLIENT_ID"] };
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
        // Bind the selected registry authority to the concrete target before composing any
        // credential or worker service. This also rejects pinned IDs outside the exact scopes.
        options.ValidateRegistryAuthority(scope);
        var managedIdentity = new Azure.Identity.ManagedIdentityCredential(
            Azure.Identity.ManagedIdentityId.FromUserAssignedClientId(options.AzureCliClientId!));
        services.AddSingleton<IAzureKeyVaultSecretReader>(_ => new AzureKeyVaultSecretReader(managedIdentity));
        services.AddScoped<IAzureSecretAuthorizationStore>(provider =>
            new DurableAzureSecretAuthorizationStore(
                provider.GetRequiredService<IAzureProviderResourceAssignmentStore>(),
                provider.GetRequiredService<IAzureProviderOperationStore>()));
        services.AddScoped<IAzureSecretResolver>(provider =>
            new ManagedIdentityAzureSecretResolver(
                provider.GetRequiredService<IAzureSecretAuthorizationStore>(),
                provider.GetRequiredService<IAzureKeyVaultSecretReader>()));
        services.AddScoped<IAzureProviderRunner>(provider =>
            new AzureBicepProviderRunner(
                options,
                scope,
                provider.GetRequiredService<IAzureSecretResolver>()));
        return new(options, scope);
    }
}
