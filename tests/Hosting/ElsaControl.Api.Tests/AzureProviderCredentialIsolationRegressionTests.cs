using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class AzureProviderCredentialIsolationRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-api-runner-baseline-{Guid.NewGuid():N}");

    [Fact]
    public void Enabled_worker_rejects_shared_external_admin_and_signing_key_vault_references()
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
            ["Deployment:AzureProvider:Secrets:0:Name"] = AzureManagedSecretReferences.DatabaseConnectionStringName,
            ["Deployment:AzureProvider:Secrets:0:Reference"] = AzureManagedSecretReferences.SqlConnection,
            ["Deployment:AzureProvider:Secrets:1:Name"] = "identity:signingkey",
            ["Deployment:AzureProvider:Secrets:1:Reference"] = KeyVaultReference("identity-signing-key"),
            ["Deployment:AzureProvider:Secrets:2:Name"] = "admin:password",
            ["Deployment:AzureProvider:Secrets:2:Reference"] = KeyVaultReference("admin-password")
        });

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => AzureProviderRunnerComposition.AddRunner(services, configuration));

        Assert.Equal("Azure provider named secret references are invalid, duplicated, or unsupported.", exception.Message);
        Assert.Empty(services);
    }

    private static string KeyVaultReference(string name) =>
        $"https://shared.vault.azure.net/secrets/{name}/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

