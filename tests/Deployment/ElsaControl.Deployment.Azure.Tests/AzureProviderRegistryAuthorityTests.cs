using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderRegistryAuthorityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-azure-registry-authority-{Guid.NewGuid():N}");

    public AzureProviderRegistryAuthorityTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "main.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "acr-pull-role.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_root, "sql-bootstrap.sql"), "SELECT 1;");
        File.WriteAllText(Path.Combine(_root, "az"), "azure-cli");
        File.WriteAllText(Path.Combine(_root, "sqlcmd"), "sqlcmd");
        File.WriteAllText(Path.Combine(_root, "curl"), "curl");
    }

    [Fact]
    public void Narrow_profile_requires_all_pinned_authority_ids_and_changes_only_the_extension_fingerprint()
    {
        var options = ValidOptions();
        var scope = ValidScope();
        Assert.True(AzureProviderRegistryAuthority.IsRoleDefinitionId(RoleDefinitionId, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        Assert.True(AzureProviderRegistryAuthority.IsExactRoleDefinitionId(RoleDefinitionId, "/subscriptions/22222222-2222-2222-2222-222222222222"));
        Assert.True(AzureProviderRegistryAuthority.IsExactRoleAssignmentId(RegistryGroupAssignmentId, "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg"));
        Assert.True(AzureProviderRegistryAuthority.IsExactRoleAssignmentId(RegistryAssignmentId, AzureProviderRegistryAuthority.RegistryScope(scope)));
        var legacyFingerprint = options.ComputeProviderScopeFingerprint(scope);

        Assert.Throws<ArgumentException>(() => (options with { RegistryAuthorityMode = AzureProviderRegistryAuthorityMode.Narrow }).Validate());

        var narrow = options with
        {
            RegistryAuthorityMode = AzureProviderRegistryAuthorityMode.Narrow,
            RegistryDeploymentMetadataRoleDefinitionId = RoleDefinitionId,
            RegistryDeploymentMetadataRoleAssignmentId = RegistryGroupAssignmentId,
            RegistryRoleAdministrationAssignmentId = RegistryAssignmentId
        };

        narrow.Validate();
        Assert.NotEqual(legacyFingerprint, narrow.ComputeProviderScopeFingerprint(scope));
        Assert.Equal(
            narrow.ComputeProviderScopeFingerprint(scope),
            (narrow with
            {
                RegistryDeploymentMetadataRoleDefinitionId = RoleDefinitionId.ToUpperInvariant(),
                RegistryDeploymentMetadataRoleAssignmentId = RegistryGroupAssignmentId.ToUpperInvariant(),
                RegistryRoleAdministrationAssignmentId = RegistryAssignmentId.ToUpperInvariant()
            }).ComputeProviderScopeFingerprint(scope));
    }

    [Theory]
    [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/providers/Microsoft.Authorization/roleDefinitions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/other/providers/Microsoft.Authorization/roleDefinitions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg/providers/Microsoft.Authorization/roleAssignments/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public void Narrow_profile_rejects_authority_ids_outside_the_exact_registry_scopes(string id)
    {
        var options = ValidOptions() with
        {
            RegistryAuthorityMode = AzureProviderRegistryAuthorityMode.Narrow,
            RegistryDeploymentMetadataRoleDefinitionId = id,
            RegistryDeploymentMetadataRoleAssignmentId = RegistryGroupAssignmentId,
            RegistryRoleAdministrationAssignmentId = RegistryAssignmentId
        };

        Assert.Throws<ArgumentException>(() => options.ComputeProviderScopeFingerprint(ValidScope()));
    }

    [Fact]
    public void Legacy_profile_fingerprint_is_byte_compatible_and_rejects_narrow_values()
    {
        var options = ValidOptions();
        var old = LegacyProviderScopeFingerprint(options, ValidScope());

        Assert.Throws<ArgumentException>(() => (options with
        {
            RegistryDeploymentMetadataRoleDefinitionId = RoleDefinitionId
        }).Validate());
        Assert.Equal(old, options.ComputeProviderScopeFingerprint(ValidScope()));
    }

    [Fact]
    public void Registry_delegation_condition_normalization_preserves_authored_literals()
    {
        var condition = AzureProviderRegistryAuthority.RegistryRoleAdministrationCondition;

        Assert.Equal(
            AzureProviderRegistryAuthority.NormalizeCondition(condition),
            AzureProviderRegistryAuthority.NormalizeCondition(condition.Replace(" OR ", "\nOR\n", StringComparison.Ordinal)));
        Assert.Equal(
            AzureProviderRegistryAuthority.NormalizeCondition(condition),
            AzureProviderRegistryAuthority.NormalizeCondition(condition.Replace(" AND ", "\r\nAND\r\n", StringComparison.Ordinal)));
        Assert.NotEqual(
            AzureProviderRegistryAuthority.NormalizeCondition(condition),
            AzureProviderRegistryAuthority.NormalizeCondition(condition.Replace("roleAssignments/write", "roleAssignments /write", StringComparison.Ordinal)));
        Assert.NotEqual(
            AzureProviderRegistryAuthority.NormalizeCondition(condition),
            AzureProviderRegistryAuthority.NormalizeCondition(condition.Replace("@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId]", "@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId]", StringComparison.Ordinal)));
        Assert.NotEqual(
            AzureProviderRegistryAuthority.NormalizeCondition(condition),
            AzureProviderRegistryAuthority.NormalizeCondition(condition[..condition.IndexOf(" AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'}", StringComparison.Ordinal)]));
    }

    private AzureProviderRunnerOptions ValidOptions() => new()
    {
        Enabled = true,
        AzureCliClientId = "33333333-3333-3333-3333-333333333333",
        AzureCliPath = Path.Combine(_root, "az"),
        SqlCmdPath = Path.Combine(_root, "sqlcmd"),
        CurlPath = Path.Combine(_root, "curl"),
        TemplateRoot = _root,
        SqlBootstrapObjectId = "11111111-1111-1111-1111-111111111111",
        SqlBootstrapLogin = "proof-bootstrap",
        SqlBootstrapIp = "203.0.113.10",
        RuntimeAdminUsername = "runtime-admin"
    };

    private static AzureProviderTargetScope ValidScope() => new(
        "11111111-1111-1111-1111-111111111111",
        "proof-rg",
        "22222222-2222-2222-2222-222222222222",
        "registry-rg",
        "valenceruntimeimages",
        "westeurope");

    private static string RoleDefinitionId => "/subscriptions/22222222-2222-2222-2222-222222222222/providers/Microsoft.Authorization/roleDefinitions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private static string RegistryGroupAssignmentId => "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg/providers/Microsoft.Authorization/roleAssignments/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static string RegistryAssignmentId => "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg/providers/Microsoft.ContainerRegistry/registries/valenceruntimeimages/providers/Microsoft.Authorization/roleAssignments/cccccccc-cccc-cccc-cccc-cccccccccccc";

    private static string LegacyProviderScopeFingerprint(AzureProviderRunnerOptions options, AzureProviderTargetScope scope)
    {
        options.Validate();
        var canonical = JsonSerializer.Serialize(new
        {
            targetScopeFingerprint = scope.ComputeFingerprint(),
            azureCliPath = Path.GetFullPath(options.AzureCliPath),
            azureCliDigest = FileDigest(options.AzureCliPath),
            azureCliClientId = options.AzureCliClientId?.ToLowerInvariant(),
            sqlCmdPath = Path.GetFullPath(options.SqlCmdPath),
            sqlCmdDigest = FileDigest(options.SqlCmdPath),
            curlPath = Path.GetFullPath(options.CurlPath),
            curlDigest = FileDigest(options.CurlPath),
            templateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.TemplateRoot)),
            templateAuthorityFingerprint = options.ComputeTemplateAuthorityFingerprint(),
            sqlBootstrapObjectId = options.SqlBootstrapObjectId.ToLowerInvariant(),
            sqlBootstrapLogin = options.SqlBootstrapLogin,
            sqlBootstrapIp = options.SqlBootstrapIp,
            runtimeAdminUsername = options.RuntimeAdminUsername,
            releaseFeedServiceIndex = options.NormalizeReleaseFeedServiceIndex(),
            disposableProofMode = options.DisposableProofMode,
            disposableExpiryUtc = options.DisposableExpiryUtc?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            owner = options.Owner.ToLowerInvariant()
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string FileDigest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
