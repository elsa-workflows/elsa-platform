using System.Text.Json;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderNarrowPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-azure-narrow-preflight-{Guid.NewGuid():N}");

    public AzureProviderNarrowPreflightTests()
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
    public async Task Narrow_profile_checks_the_pinned_definition_and_exact_assignments()
    {
        var process = new FakeCommandProcess();
        var result = await Preflight(process).ValidateAsync();

        Assert.True(result.Succeeded, $"{result.Code}: {result.Message}");
        Assert.Equal("azure.preflight.succeeded", result.Code);
        Assert.Contains(process.Calls, arguments => arguments[0] == "rest" &&
            arguments.Contains("https://management.azure.com/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg/providers/Microsoft.Authorization/roleDefinitions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?api-version=2022-04-01"));

        var roleLists = process.Calls.Where(arguments => arguments is ["role", "assignment", "list", ..]).ToArray();
        Assert.Equal(3, roleLists.Length);
        Assert.All(roleLists, arguments =>
        {
            Assert.DoesNotContain("--all", arguments);
            Assert.Contains("--scope", arguments);
            Assert.Contains("--assignee-object-id", arguments);
            Assert.Contains("--include-inherited", arguments);
            Assert.Contains("--fill-principal-name", arguments);
            Assert.Contains("--fill-role-definition-name", arguments);
        });
        Assert.Contains(roleLists, arguments => arguments.Contains(RegistryGroupScope));
        Assert.Contains(roleLists, arguments => arguments.Contains(RegistryScope));
    }

    [Fact]
    public async Task Narrow_profile_rejects_an_extra_metadata_action()
    {
        var process = new FakeCommandProcess { RoleDefinitionOutput = RoleDefinitionJson(extraAction: true) };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.rbac-insufficient", result.Code);
    }

    [Fact]
    public async Task Narrow_profile_rejects_a_mismatched_delegation_condition()
    {
        var process = new FakeCommandProcess
        {
            RegistryAssignmentOutput = AssignmentJson(
                RegistryAssignmentId,
                RegistryScope,
                AzureProviderRegistryAuthority.RbacAdministratorRoleDefinitionId,
                "2.0",
                AzureProviderRegistryAuthority.RegistryRoleAdministrationCondition + " OR false")
        };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.rbac-insufficient", result.Code);
    }

    [Theory]
    [InlineData("action-literal-whitespace")]
    [InlineData("missing-delete-clause")]
    [InlineData("wrong-delete-source")]
    [InlineData("extra-disjunction")]
    public async Task Narrow_profile_rejects_condition_variants(string variant)
    {
        var condition = AzureProviderRegistryAuthority.RegistryRoleAdministrationCondition;
        condition = variant switch
        {
            "action-literal-whitespace" => condition.Replace("roleAssignments/write", "roleAssignments /write", StringComparison.Ordinal),
            "missing-delete-clause" => condition[..condition.IndexOf(" AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'}", StringComparison.Ordinal)],
            "wrong-delete-source" => condition.Replace("@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId]", "@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId]", StringComparison.Ordinal),
            "extra-disjunction" => condition + " OR false",
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var process = new FakeCommandProcess
        {
            RegistryAssignmentOutput = AssignmentJson(
                RegistryAssignmentId,
                RegistryScope,
                AzureProviderRegistryAuthority.RbacAdministratorRoleDefinitionId,
                AzureProviderRegistryAuthority.RegistryRoleAdministrationConditionVersion,
                condition)
        };

        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.rbac-insufficient", result.Code);
    }

    [Fact]
    public async Task Narrow_profile_does_not_fallback_when_role_definition_observation_is_denied()
    {
        var process = new FakeCommandProcess { FailRoleDefinitionObservation = true };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.observation-failed", result.Code);
        Assert.DoesNotContain(process.Calls, arguments => arguments is ["role", "assignment", "list", ..] && arguments.Contains("Contributor"));
    }

    private AzureProviderAuthorityPreflight Preflight(FakeCommandProcess process) =>
        new(Options(), Scope(), process);

    private AzureProviderRunnerOptions Options() => new()
    {
        Enabled = true,
        AzureCliClientId = PrincipalId,
        AzureCliPath = Path.Combine(_root, "az"),
        SqlCmdPath = Path.Combine(_root, "sqlcmd"),
        CurlPath = Path.Combine(_root, "curl"),
        TemplateRoot = _root,
        SqlBootstrapObjectId = PrincipalId,
        SqlBootstrapLogin = "proof-bootstrap",
        SqlBootstrapIp = "203.0.113.10",
        RuntimeAdminUsername = "runtime-admin",
        RegistryAuthorityMode = AzureProviderRegistryAuthorityMode.Narrow,
        RegistryDeploymentMetadataRoleDefinitionId = RoleDefinitionId,
        RegistryDeploymentMetadataRoleAssignmentId = RegistryGroupAssignmentId,
        RegistryRoleAdministrationAssignmentId = RegistryAssignmentId
    };

    private static AzureProviderTargetScope Scope() => new(
        "11111111-1111-1111-1111-111111111111",
        "proof-rg",
        "22222222-2222-2222-2222-222222222222",
        "registry-rg",
        "runtimeimages",
        "westeurope");

    private const string PrincipalId = "33333333-3333-3333-3333-333333333333";
    private const string RoleDefinitionId = "/subscriptions/22222222-2222-2222-2222-222222222222/providers/Microsoft.Authorization/roleDefinitions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string RegistryGroupAssignmentId = RegistryGroupScope + "/providers/Microsoft.Authorization/roleAssignments/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string RegistryAssignmentId = RegistryScope + "/providers/Microsoft.Authorization/roleAssignments/cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string RegistryGroupScope = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/registry-rg";
    private const string RegistryScope = RegistryGroupScope + "/providers/Microsoft.ContainerRegistry/registries/runtimeimages";

    private static string RoleDefinitionJson(bool extraAction = false)
    {
        var actions = AzureProviderRegistryAuthority.NarrowMetadataActions.ToList();
        if (extraAction)
            actions.Add("Microsoft.Resources/resourceGroups/delete");
        return JsonSerializer.Serialize(new
        {
            id = RoleDefinitionId,
            type = "CustomRole",
            assignableScopes = new[] { RegistryGroupScope },
            permissions = new[]
            {
                new
                {
                    actions,
                    notActions = Array.Empty<string>(),
                    dataActions = Array.Empty<string>(),
                    notDataActions = Array.Empty<string>()
                }
            }
        });
    }

    private static string AssignmentJson(string id, string scope, string roleDefinitionId, string? conditionVersion = null, string? condition = null) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                id,
                scope,
                principalId = PrincipalId,
                principalType = "ServicePrincipal",
                roleDefinitionId = roleDefinitionId.Contains("/providers/Microsoft.Authorization/roleDefinitions/", StringComparison.OrdinalIgnoreCase)
                    ? roleDefinitionId
                    : $"/subscriptions/22222222-2222-2222-2222-222222222222/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionId}",
                condition,
                conditionVersion
            }
        });

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeCommandProcess : IAzureCommandProcess
    {
        public List<string[]> Calls { get; } = [];
        public bool FailRoleDefinitionObservation { get; init; }
        public string RoleDefinitionOutput { get; init; } = RoleDefinitionJson();
        public string RegistryAssignmentOutput { get; init; } = AssignmentJson(
            RegistryAssignmentId,
            RegistryScope,
            AzureProviderRegistryAuthority.RbacAdministratorRoleDefinitionId,
            AzureProviderRegistryAuthority.RegistryRoleAdministrationConditionVersion,
            AzureProviderRegistryAuthority.RegistryRoleAdministrationCondition);

        public Task<AzureCommandProcessResult<T>> ExecuteAsync<T>(
            AzureCommandProcessRequest request,
            AzureCommandOutputProjector<T> outputProjector,
            CancellationToken cancellationToken = default)
            where T : AzureCommandSafeOutput
        {
            var arguments = request.Arguments.Select(argument => argument.Value).ToArray();
            Calls.Add(arguments);
            if (arguments[0] == "rest" && FailRoleDefinitionObservation)
                return Task.FromResult(Failed<T>());

            var output = arguments[0] switch
            {
                "account" when arguments[1] == "show" => "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"userAssignedIdentity\",\"type\":\"servicePrincipal\",\"identity\":\"MSIClient-33333333-3333-3333-3333-333333333333\"}",
                "identity" => "[\"33333333-3333-3333-3333-333333333333\"]",
                "group" => "true",
                "rest" => RoleDefinitionOutput,
                "role" when arguments.Contains(RegistryScope) => RegistryAssignmentOutput,
                "role" when arguments.Contains(RegistryGroupScope) => AssignmentJson(RegistryGroupAssignmentId, RegistryGroupScope, RoleDefinitionId),
                "role" => "[\"/subscriptions/11111111-1111-1111-1111-111111111111/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c\",\"/subscriptions/11111111-1111-1111-1111-111111111111/providers/Microsoft.Authorization/roleDefinitions/f58310d9-a9f6-439a-9e8d-f62e7b41a168\"]",
                _ => string.Empty
            };
            return Task.FromResult(new AzureCommandProcessResult<T>(
                AzureCommandProcessStatus.Succeeded,
                AzureCommandProcessFailureKind.None,
                0,
                outputProjector(output.AsMemory()),
                "azure.command.succeeded",
                "The Azure command completed successfully."));
        }

        private static AzureCommandProcessResult<T> Failed<T>() where T : AzureCommandSafeOutput => new(
            AzureCommandProcessStatus.Failed,
            AzureCommandProcessFailureKind.NonZeroExitCode,
            1,
            null,
            "azure.command.failed",
            "The Azure command failed.");
    }
}
