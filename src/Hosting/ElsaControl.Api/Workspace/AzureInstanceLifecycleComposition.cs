using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Composes the optional Azure lifecycle adapter only when its explicit authority
/// is enabled and valid. Keeping this decision in one production seam makes it
/// testable without starting the complete API host.
/// </summary>
internal static class AzureInstanceLifecycleComposition
{
    public static bool AddProviderPorts(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(AzureElsaInstanceProviderOptions.ConfigurationSection);
        services.Configure<AzureElsaInstanceProviderOptions>(section);
        services.AddScoped<AzureElsaInstanceProviderOptions>(provider =>
            provider.GetRequiredService<IOptions<AzureElsaInstanceProviderOptions>>().Value);

        var enabled = section.GetValue<bool>(nameof(AzureElsaInstanceProviderOptions.Enabled));
        if (!enabled)
            return false;

        // Do not let a partially configured provider reach the lifecycle worker.
        // The fingerprint binds each durable operation to the exact provider
        // authority; without it startup must fail closed.
        new AzureElsaInstanceProviderOptions
        {
            Enabled = true,
            TemplateFingerprint = section.GetValue<string>(nameof(AzureElsaInstanceProviderOptions.TemplateFingerprint))
                ?? AzureElsaInstanceProviderOptions.DefaultTemplateFingerprint,
            ProviderScopeFingerprint = section.GetValue<string>(nameof(AzureElsaInstanceProviderOptions.ProviderScopeFingerprint))
        }.Validate();

        services.AddScoped<AzureElsaInstanceProvider>();
        services.AddScoped<IElsaInstanceProviderSubmissionPort>(provider =>
            provider.GetRequiredService<AzureElsaInstanceProvider>());
        services.AddScoped<IElsaInstanceProviderReconciliationPort>(provider =>
            provider.GetRequiredService<AzureElsaInstanceProvider>());
        return true;
    }
}
