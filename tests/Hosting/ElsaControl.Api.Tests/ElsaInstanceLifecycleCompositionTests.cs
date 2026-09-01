using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Api.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class ElsaInstanceLifecycleCompositionTests
{
    [Fact]
    public void Azure_provider_ports_are_not_composed_when_Azure_lifecycle_is_disabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:ElsaInstanceLifecycle:Enabled"] = "true",
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "false"
        });

        Assert.False(AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderSubmissionPort));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderReconciliationPort));
    }

    [Fact]
    public void Azure_provider_ports_require_explicit_provider_authority_when_enabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "true",
            ["Deployment:AzureProvider:InstanceLifecycle:ProviderScopeFingerprint"] = new string('a', 64)
        });

        Assert.True(AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderSubmissionPort));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderReconciliationPort));
    }

    [Fact]
    public void Enabled_Azure_lifecycle_fails_closed_without_a_valid_scope_fingerprint()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "true"
        });

        Assert.Throws<ArgumentException>(() =>
            AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration));
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
