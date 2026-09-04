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
    public void Enabled_worker_rejects_raw_secret_values_before_composing_the_runner()
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
            ["Deployment:AzureProvider:Runner:RuntimeAdminUsername"] = "runtime-admin",
            ["AZURE_CLIENT_ID"] = "33333333-3333-3333-3333-333333333333",
            ["Deployment:AzureProvider:Runner:TargetScope:SubscriptionId"] = "11111111-1111-1111-1111-111111111111",
            ["Deployment:AzureProvider:Runner:TargetScope:ResourceGroupName"] = "proof-rg",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistrySubscriptionId"] = "22222222-2222-2222-2222-222222222222",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistryResourceGroupName"] = "registry-rg",
            ["Deployment:AzureProvider:Runner:TargetScope:RegistryName"] = "valenceruntimeimages",
            ["Deployment:AzureProvider:Runner:TargetScope:Location"] = "westeurope",
            ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/database",
            ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
            ["Deployment:AzureProvider:Secrets:0:Value"] = "runtime-only-secret",
            ["Deployment:AzureProvider:Secrets:1:Reference"] = "secret://vault/identity-signing-key",
            ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
            ["Deployment:AzureProvider:Secrets:1:Value"] = "runtime-only-signing-key",
            ["Deployment:AzureProvider:Secrets:2:Reference"] = "secret://vault/admin-password",
            ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
            ["Deployment:AzureProvider:Secrets:2:Value"] = "runtime-only-admin-password"
        });
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AzureProviderRunnerComposition.AddRunner(services, configuration));

        Assert.Equal("Azure provider worker configuration must not contain raw secret values.", exception.Message);
    }

    [Fact]
    public void Named_secret_references_project_only_safe_locators_for_lifecycle_resolution()
    {
        var references = ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = KeyVaultReference("sql-connection"),
                ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("identity-signing-key"),
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password"),
                ["Deployment:AzureProvider:Secrets:2:Value"] = "runtime-only-admin-password"
            }));

        Assert.Equal(3, references.Count);
        Assert.Equal(KeyVaultReference("sql-connection"), references["database:connectionstring"]);
        Assert.DoesNotContain(references.Values, value => value.Contains("runtime-only", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)references).Add("extra", KeyVaultReference("extra")));
    }

    [Fact]
    public void Named_secret_references_allow_the_fixed_provider_owned_sql_reference_only_for_the_database_slot()
    {
        var references = ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = AzureManagedSecretReferences.SqlConnection,
                ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("identity-signing-key"),
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password")
            }));

        Assert.Equal(AzureManagedSecretReferences.SqlConnection, references["database:connectionstring"]);
    }

    [Fact]
    public void Named_secret_references_reject_the_provider_owned_sql_reference_for_another_slot()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = AzureManagedSecretReferences.SqlConnection,
                ["Deployment:AzureProvider:Secrets:1:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("sql-connection"),
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password")
            })));

        Assert.Equal("Azure provider named secret references are invalid, duplicated, or unsupported.", exception.Message);
        Assert.DoesNotContain(AzureManagedSecretReferences.SqlConnection, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_secret_references_fail_closed_when_required_binding_is_missing()
    {
        Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = KeyVaultReference("sql-connection")
            })));
    }

    [Fact]
    public void Named_secret_references_need_no_raw_value_binding()
    {
        var references = ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = KeyVaultReference("sql-connection"),
                ["Deployment:AzureProvider:Secrets:0:Value"] = "runtime-only-secret",
                ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("identity-signing-key"),
                ["Deployment:AzureProvider:Secrets:1:Value"] = "runtime-only-signing-key",
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password")
            }));

        Assert.Equal(3, references.Count);
    }

    [Fact]
    public void Named_secret_references_fail_closed_when_a_reference_is_shared_by_aliases()
    {
        Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = KeyVaultReference("shared"),
                ["Deployment:AzureProvider:Secrets:0:Value"] = "runtime-only-database",
                ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("shared"),
                ["Deployment:AzureProvider:Secrets:1:Value"] = "runtime-only-identity",
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password"),
                ["Deployment:AzureProvider:Secrets:2:Value"] = "runtime-only-admin-password"
            })));
    }

    [Fact]
    public void Named_secret_references_reject_provider_neutral_locators_with_a_safe_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ConfiguredAzureSecretResolver.ReadNamedReferences(
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/sql-connection",
                ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
                ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("identity-signing-key"),
                ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
                ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password")
            })));

        Assert.Equal("Azure provider named secret references are invalid, duplicated, or unsupported.", exception.Message);
        Assert.DoesNotContain("secret://", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vault", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enabled_worker_rejects_unsupported_named_secret_locators_before_runner_composition()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => AzureProviderRunnerComposition.AddRunner(services,
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:WorkerEnabled"] = "true",
                ["Deployment:AzureProvider:Secrets:0:Name"] = "database:connectionstring",
                ["Deployment:AzureProvider:Secrets:0:Reference"] = "secret://vault/sql-connection"
            })));

        Assert.Equal("Azure provider named secret references are invalid, duplicated, or unsupported.", exception.Message);
        Assert.DoesNotContain("secret://", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "provider-assignment",
            "database:connectionstring", "secret://vault/database"));

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

    [Fact]
    public void Enabled_worker_rejects_disposable_proof_mode_before_composing_production_runner()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => AzureProviderRunnerComposition.AddRunner(services,
            Configuration(new Dictionary<string, string?>
            {
                ["Deployment:AzureProvider:WorkerEnabled"] = "true",
                ["Deployment:AzureProvider:Runner:DisposableProofMode"] = "true"
            })));

        Assert.Equal("The production Azure provider worker must not use disposable proof mode.", exception.Message);
    }

    private static string KeyVaultReference(string name) =>
        $"https://source.vault.azure.net/secrets/{name}/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
