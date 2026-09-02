using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Api.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class ElsaInstanceLifecycleCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-api-lifecycle-{Guid.NewGuid():N}");

    [Fact]
    public void Azure_provider_ports_are_not_composed_when_Azure_lifecycle_is_disabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:ElsaInstanceLifecycle:Enabled"] = "true",
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "false"
        });

        Assert.False(AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration, null));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderSubmissionPort));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderReconciliationPort));
    }

    [Fact]
    public void Azure_provider_ports_derive_the_exact_validated_runner_authority_when_enabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "true"
        });
        var authority = Authority();

        Assert.True(AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration, authority));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderSubmissionPort));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderReconciliationPort));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AzureElsaInstanceProviderOptions>();
        Assert.Equal(authority.TemplateFingerprint, options.TemplateFingerprint);
        Assert.Equal(authority.ProviderScopeFingerprint, options.ProviderScopeFingerprint);
    }

    [Fact]
    public void Enabled_Azure_lifecycle_fails_closed_without_the_concrete_runner_authority()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:InstanceLifecycle:Enabled"] = "true"
        });

        Assert.Throws<InvalidOperationException>(() =>
            AzureInstanceLifecycleComposition.AddProviderPorts(services, configuration, null));
    }

    private AzureProviderRunnerAuthority Authority()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "main.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "acr-pull-role.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "sql-bootstrap.sql"), "SELECT 1;");
        var tool = Environment.ProcessPath ?? "/bin/sh";
        var options = new AzureProviderRunnerOptions
        {
            Enabled = true,
            AzureCliPath = tool,
            SqlCmdPath = tool,
            CurlPath = tool,
            TemplateRoot = _root,
            SqlBootstrapObjectId = "11111111-1111-1111-1111-111111111111",
            SqlBootstrapLogin = "bootstrap",
            SqlBootstrapIp = "203.0.113.10",
            ExpiryUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        var scope = new AzureProviderTargetScope(
            "11111111-1111-1111-1111-111111111111",
            "proof-rg",
            "22222222-2222-2222-2222-222222222222",
            "registry-rg",
            "valenceruntimeimages",
            "westeurope");
        return new(options, scope);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
