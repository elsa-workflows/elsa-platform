using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Api.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Api.Tests;

public sealed class ElsaInstanceLifecycleCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-api-lifecycle-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null, "restricted")]
    [InlineData("restricted", "restricted")]
    [InlineData("unrestricted", "unrestricted")]
    public void Instance_plan_egress_requires_an_explicit_server_policy_to_relax_the_default(string? configured, string expected)
    {
        var services = new ServiceCollection();
        ElsaInstancePlanResolutionComposition.AddResolver(services, Configuration(new Dictionary<string, string?>
        {
            ["RuntimeBuilder:InstancePlans:DefaultEgress"] = configured
        }));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetRequiredService<ElsaInstancePlanResolutionOptions>().DefaultEgress);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IElsaInstancePlanResolver));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-policy")]
    [InlineData("unrestricted\nsecret-marker")]
    public void Unsupported_instance_plan_egress_fails_closed_without_echoing_configuration(string configured)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ElsaInstancePlanResolutionComposition.AddResolver(services, Configuration(new Dictionary<string, string?>
            {
                ["RuntimeBuilder:InstancePlans:DefaultEgress"] = configured
            })));

        Assert.Equal("The instance plan egress policy is unsupported.", exception.Message);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IElsaInstancePlanResolver));
    }

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
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderCleanupPort));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderRecoveryPort));
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
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderCleanupPort));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsaInstanceProviderRecoveryPort));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AzureElsaInstanceProviderOptions>();
        Assert.Equal(authority.TemplateFingerprint, options.TemplateFingerprint);
        Assert.Equal(authority.ProviderScopeFingerprint, options.ProviderScopeFingerprint);
        Assert.Equal(authority.Scope.SubscriptionId, options.SubscriptionId);
        Assert.Equal(authority.Scope.ResourceGroupName, options.ResourceGroupNamePrefix);
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
            AzureCliClientId = "33333333-3333-3333-3333-333333333333",
            SqlCmdPath = tool,
            CurlPath = tool,
            TemplateRoot = _root,
            SqlBootstrapObjectId = "11111111-1111-1111-1111-111111111111",
            SqlBootstrapLogin = "bootstrap",
            SqlBootstrapIp = "203.0.113.10",
            RuntimeAdminUsername = "runtime-admin"
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
