using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderAuthorityPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-azure-preflight-{Guid.NewGuid():N}");

    public AzureProviderAuthorityPreflightTests()
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
    public async Task Authenticates_managed_identity_and_checks_both_mutation_scopes()
    {
        var process = new FakeCommandProcess();
        var result = await Preflight(process).ValidateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("azure.preflight.succeeded", result.Code);

        var login = Assert.Single(process.Calls, arguments => arguments[0] == "login");
        Assert.Contains("--identity", login);
        Assert.Contains("--allow-no-subscriptions", login);
        Assert.Equal("11111111-1111-1111-1111-111111111111", login[Array.IndexOf(login, "--client-id") + 1]);

        var roleLists = process.Calls.Where(arguments => arguments is ["role", "assignment", "list", ..]).ToArray();
        Assert.Equal(3, roleLists.Length);
        Assert.Contains(
            roleLists,
            arguments => arguments.Contains("--scope") &&
                         arguments.Contains("/subscriptions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/resourceGroups/proof-rg"));
        Assert.Contains(
            roleLists,
            arguments => arguments.Contains("--scope") &&
                         arguments.Contains("/subscriptions/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/resourceGroups/registry-rg") &&
                         !arguments.Contains("runtimeimages"));
        Assert.Contains(
            roleLists,
            arguments => arguments.Contains("--scope") &&
                         arguments.Contains("/subscriptions/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/resourceGroups/registry-rg/providers/Microsoft.ContainerRegistry/registries/runtimeimages"));
    }

    [Fact]
    public async Task Authentication_failure_stops_before_rbac_observation()
    {
        var process = new FakeCommandProcess { FailLogin = true };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.authentication-failed", result.Code);
        Assert.DoesNotContain(process.Calls, arguments => arguments[0] == "role");
    }

    [Fact]
    public async Task Missing_required_role_fails_closed()
    {
        var process = new FakeCommandProcess { TargetRoles = ["Contributor"] };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.rbac-insufficient", result.Code);
    }

    [Fact]
    public async Task Existing_target_group_is_authorized_at_the_resource_group_scope()
    {
        var process = new FakeCommandProcess { TargetGroupExists = true };
        var result = await Preflight(process).ValidateAsync();

        Assert.True(result.Succeeded);
        Assert.Contains(
            process.Calls,
            arguments => arguments is ["role", "assignment", "list", ..] &&
                         arguments.Contains("/subscriptions/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/resourceGroups/proof-rg"));
    }

    [Fact]
    public async Task Missing_governed_target_group_fails_without_subscription_scope_fallback()
    {
        var process = new FakeCommandProcess { TargetGroupExists = false };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.observation-invalid", result.Code);
        Assert.DoesNotContain(process.Calls, arguments => arguments is ["role", "assignment", "list", ..]);
    }

    [Fact]
    public async Task Invalid_identity_observation_fails_without_using_untrusted_assignee()
    {
        var process = new FakeCommandProcess { AccountOutput = "{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"principal\":\"not a safe identity\"}" };
        var result = await Preflight(process).ValidateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("azure.preflight.observation-invalid", result.Code);
        Assert.DoesNotContain(process.Calls, arguments => arguments[0] == "role");
    }

    private AzureProviderAuthorityPreflight Preflight(FakeCommandProcess process) =>
        new(Options(), Scope(), process);

    private AzureProviderRunnerOptions Options() => new()
    {
        Enabled = true,
        AzureCliClientId = "11111111-1111-1111-1111-111111111111",
        AzureCliPath = Path.Combine(_root, "az"),
        SqlCmdPath = Path.Combine(_root, "sqlcmd"),
        CurlPath = Path.Combine(_root, "curl"),
        TemplateRoot = _root,
        SqlBootstrapObjectId = "11111111-1111-1111-1111-111111111111",
        SqlBootstrapLogin = "proof-bootstrap",
        SqlBootstrapIp = "203.0.113.10",
        RuntimeAdminUsername = "runtime-admin"
    };

    private static AzureProviderTargetScope Scope() => new(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "proof-rg",
        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "registry-rg",
        "runtimeimages",
        "westeurope");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeCommandProcess : IAzureCommandProcess
    {
        public List<string[]> Calls { get; } = [];
        public bool FailLogin { get; init; }
        public bool TargetGroupExists { get; init; } = true;
        public string AccountOutput { get; init; } = "{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"principal\":\"11111111-1111-1111-1111-111111111111\"}";
        public IReadOnlyList<string> TargetRoles { get; init; } = ["Contributor", "Role Based Access Control Administrator"];
        public IReadOnlyList<string> RegistryRoles { get; init; } = ["Contributor", "Role Based Access Control Administrator"];

        public Task<AzureCommandProcessResult<T>> ExecuteAsync<T>(
            AzureCommandProcessRequest request,
            AzureCommandOutputProjector<T> outputProjector,
            CancellationToken cancellationToken = default)
            where T : AzureCommandSafeOutput
        {
            var arguments = request.Arguments.Select(argument => argument.Value).ToArray();
            Calls.Add(arguments);
            if (arguments[0] == "login" && FailLogin)
                return Task.FromResult(Failed<T>());

            var output = arguments[0] switch
            {
                "account" when arguments[1] == "show" => AccountOutput,
                "group" => TargetGroupExists ? "true" : "false",
                "role" when arguments.Contains("runtimeimages") => $"[{string.Join(',', RegistryRoles.Select(JsonString))}]",
                "role" => $"[{string.Join(',', TargetRoles.Select(JsonString))}]",
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

        private static string JsonString(string value) => $"\"{value}\"";
    }
}
