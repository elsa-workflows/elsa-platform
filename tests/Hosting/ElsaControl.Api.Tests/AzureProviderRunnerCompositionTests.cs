using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ElsaControl.Api.Tests;

public sealed class AzureProviderRunnerCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-api-runner-{Guid.NewGuid():N}");

    [Fact]
    public void Disabled_worker_keeps_the_provider_fail_closed()
    {
        var services = new ServiceCollection();

        var authority = AzureProviderRunnerComposition.AddRunner(services, Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:WorkerEnabled"] = "false",
            ["Deployment:AzureProvider:Runner:Enabled"] = "true"
        }));

        Assert.Null(authority);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<UnconfiguredAzureProviderRunner>(scope.ServiceProvider.GetRequiredService<IAzureProviderRunner>());
    }

    [Fact]
    public void Enabled_worker_composes_the_validated_concrete_runner()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "main.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "acr-pull-role.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "sql-bootstrap.sql"), "SELECT 1;");
        var tool = Environment.ProcessPath ?? "/bin/sh";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Deployment:AzureProvider:WorkerEnabled"] = "true",
            ["Deployment:AzureProvider:Runner:Enabled"] = "true",
            ["Deployment:AzureProvider:Runner:AzureCliPath"] = tool,
            ["Deployment:AzureProvider:Runner:SqlCmdPath"] = tool,
            ["Deployment:AzureProvider:Runner:CurlPath"] = tool,
            ["Deployment:AzureProvider:Runner:TemplateRoot"] = _root,
            ["Deployment:AzureProvider:Runner:SqlBootstrapObjectId"] = "11111111-1111-1111-1111-111111111111",
            ["Deployment:AzureProvider:Runner:SqlBootstrapLogin"] = "bootstrap",
            ["Deployment:AzureProvider:Runner:SqlBootstrapIp"] = "203.0.113.10",
            ["Deployment:AzureProvider:Runner:ExpiryUtc"] = "2026-09-02",
            ["Deployment:AzureProvider:Runner:TargetScope:SubscriptionId"] = "11111111-1111-1111-1111-111111111111",
            ["Deployment:AzureProvider:Runner:TargetScope:ResourceGroupName"] = "proof-rg",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistrySubscriptionId"] = "22222222-2222-2222-2222-222222222222",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistryResourceGroupName"] = "registry-rg",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistryName"] = "valenceruntimeimages",
            ["Deployment:AzureProvider:Runner:TargetScope:Location"] = "westeurope",
            ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/database",
            ["Deployment:AzureProvider:Secrets:0:Value"] = "runtime-only-secret",
            ["Deployment:AzureProvider:Secrets:1:Reference"] = "secret://vault/identity-signing-key",
            ["Deployment:AzureProvider:Secrets:1:Value"] = "runtime-only-signing-key",
            ["Deployment:AzureProvider:Secrets:2:Reference"] = "secret://vault/admin-password",
            ["Deployment:AzureProvider:Secrets:2:Value"] = "runtime-only-admin-password"
        });
        var services = new ServiceCollection();

        var authority = AzureProviderRunnerComposition.AddRunner(services, configuration);

        Assert.NotNull(authority);
        Assert.Equal(authority.Options.ComputeTemplateAuthorityFingerprint(), authority.TemplateFingerprint);
        Assert.Equal(authority.Options.ComputeProviderScopeFingerprint(authority.Scope), authority.ProviderScopeFingerprint);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<AzureBicepProviderRunner>(scope.ServiceProvider.GetRequiredService<IAzureProviderRunner>());
        Assert.IsType<ConfiguredAzureSecretResolver>(scope.ServiceProvider.GetRequiredService<IAzureSecretResolver>());
    }

    [Fact]
    public async Task Configured_resolver_preserves_secret_whitespace_without_exposing_value()
    {
        const string secret = "  runtime-only-secret\t ";
        var resolver = Assert.IsType<ConfiguredAzureSecretResolver>(ConfiguredAzureSecretResolver.Create(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/database",
                ["Deployment:AzureProvider:Secrets:0:Value"] = secret
            })));

        await using var lease = await resolver.ResolveAsync(new(
            Guid.NewGuid(), "database:connectionstring", "secret://vault/database"));

        Assert.Equal(secret, lease.Value.ToString());
        Assert.Equal(nameof(AzureSecretLease), lease.ToString());
        Assert.DoesNotContain(secret, lease.ToString(), StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(lease));
    }

    [Fact]
    public void Configured_resolver_rejects_case_insensitive_duplicate_aliases()
    {
        Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.Create(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/database",
                ["Deployment:AzureProvider:Secrets:0:Value"] = "first",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = "SECRET://VAULT/DATABASE",
                ["Deployment:AzureProvider:Secrets:1:Value"] = "second"
            })));
    }

    [Fact]
    public void Configured_resolver_rejects_empty_whitespace_oversized_and_nul_values()
    {
        foreach (var value in new[] { "", " \t ", new string('x', 4097), "secret\0value" })
        {
            Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.Create(
                Configuration(new Dictionary<string, string?>
                {
                    ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/database",
                    ["Deployment:AzureProvider:Secrets:0:Value"] = value
                })));
        }
    }

    [Fact]
    public void Enabled_worker_fails_closed_when_runner_authority_is_missing()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => AzureProviderRunnerComposition.AddRunner(services,
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:WorkerEnabled"] = "true",
                ["Deployment:AzureProvider:Runner:Enabled"] = "false"
            })));
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
