using System.Text.Json;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Safe startup result for the managed Azure provider authority. It deliberately contains only
/// stable classifications; Azure CLI output and identity details never cross this boundary.
/// </summary>
public sealed record AzureProviderAuthorityPreflightResult(
    bool Succeeded,
    string Code,
    string Message);

/// <summary>Checks the managed identity session and the exact Azure scopes the runner mutates.</summary>
public interface IAzureProviderAuthorityPreflight
{
    Task<AzureProviderAuthorityPreflightResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs a read-only authority check after establishing an Azure CLI managed-identity session.
/// The login may update the CLI's local token cache, but the preflight never creates or changes an
/// Azure resource. The concrete runner uses the same CLI executable and process environment.
/// </summary>
public sealed class AzureProviderAuthorityPreflight : IAzureProviderAuthorityPreflight
{
    private const string AuthenticationFailureCode = "azure.preflight.authentication-failed";
    private const string ObservationFailureCode = "azure.preflight.observation-failed";
    private const string ObservationInvalidCode = "azure.preflight.observation-invalid";
    private const string RbacInsufficientCode = "azure.preflight.rbac-insufficient";

    private static readonly HashSet<string> MutationRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Contributor",
        "Owner"
    };

    private static readonly HashSet<string> RoleAssignmentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Owner",
        "User Access Administrator",
        "Role Based Access Control Administrator"
    };

    private readonly AzureProviderRunnerOptions _options;
    private readonly AzureProviderTargetScope _scope;
    private readonly IAzureCommandProcess _process;

    public AzureProviderAuthorityPreflight(
        AzureProviderRunnerOptions options,
        AzureProviderTargetScope scope)
        : this(options, scope, new AzureCommandProcess(options.CommandTimeout, options.MaximumOutputCharacters))
    {
    }

    internal AzureProviderAuthorityPreflight(
        AzureProviderRunnerOptions options,
        AzureProviderTargetScope scope,
        IAzureCommandProcess process)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _options.Validate();
        _scope.Validate();
    }

    public async Task<AzureProviderAuthorityPreflightResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Recheck the local authority immediately before login. This closes the gap between
            // DI composition and hosted-service startup if a mounted image was altered meanwhile.
            try
            {
                _options.Validate();
                _scope.Validate();
            }
            catch (ArgumentException)
            {
                return Failed("azure.preflight.configuration-invalid", "The managed Azure provider authority configuration is invalid.");
            }
            catch (InvalidOperationException)
            {
                return Failed("azure.preflight.configuration-invalid", "The managed Azure provider authority configuration is invalid.");
            }
            cancellationToken.ThrowIfCancellationRequested();

            var loginArguments = new List<string>
            {
                "login",
                "--identity",
                "--allow-no-subscriptions"
            };
            loginArguments.Add("--client-id");
            loginArguments.Add(_options.AzureCliClientId!);
            loginArguments.AddRange(["--output", "none", "--only-show-errors"]);

            var loginFailure = await RunNoOutputAsync(
                loginArguments,
                AuthenticationFailureCode,
                "The managed Azure identity could not authenticate.",
                cancellationToken);
            if (loginFailure is not null)
                return loginFailure;

            var accountSetFailure = await RunNoOutputAsync(
                ["account", "set", "--subscription", _scope.SubscriptionId, "--only-show-errors"],
                "azure.preflight.subscription-selection-failed",
                "The managed Azure identity could not select the configured subscription.",
                cancellationToken);
            if (accountSetFailure is not null)
                return accountSetFailure;

            var account = await RunAccountObservationAsync(cancellationToken);
            if (account is null)
                return Failed(ObservationFailureCode, "The managed Azure identity account could not be observed.");
            if (!string.Equals(account.SubscriptionId, _scope.SubscriptionId, StringComparison.Ordinal) ||
                !string.Equals(account.PrincipalId, _options.AzureCliClientId, StringComparison.Ordinal))
                return Failed(ObservationInvalidCode, "The authenticated Azure identity did not match the configured authority.");

            var targetGroupExists = await RunBooleanObservationAsync(
                ["group", "exists", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName,
                    "--output", "tsv", "--only-show-errors"],
                cancellationToken);
            if (targetGroupExists is null)
                return Failed(ObservationFailureCode, "The configured Azure target scope could not be observed.");

            if (!targetGroupExists.Value)
                return Failed(ObservationInvalidCode, "The governed Azure target scope does not exist.");

            var targetScope = ResourceGroupScope(_scope.SubscriptionId, _scope.ResourceGroupName);
            var targetRoles = await RunRoleObservationAsync(targetScope, account.PrincipalId, cancellationToken);
            if (targetRoles is null)
                return Failed(ObservationFailureCode, "The configured Azure target permissions could not be observed.");
            if (!HasRequiredRoles(targetRoles))
                return Failed(RbacInsufficientCode, "The managed Azure identity lacks the required target-scope permissions.");

            // The registry phase submits a resource-group deployment, then creates a role
            // assignment at the registry resource scope. Check both scopes: a Contributor role
            // assigned only to the registry resource cannot submit the group deployment, while a
            // role-assignment role assigned only to the group does not authorize the child scope.
            var registryGroupRoles = await RunRoleObservationAsync(
                ResourceGroupScope(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName),
                account.PrincipalId,
                cancellationToken);
            if (registryGroupRoles is null)
                return Failed(ObservationFailureCode, "The configured Azure registry resource group permissions could not be observed.");
            if (!HasMutationRole(registryGroupRoles))
                return Failed(RbacInsufficientCode, "The managed Azure identity lacks registry deployment permissions.");

            var registryResourceRoles = await RunRoleObservationAsync(RegistryScope(_scope), account.PrincipalId, cancellationToken);
            if (registryResourceRoles is null)
                return Failed(ObservationFailureCode, "The configured Azure registry role-assignment permissions could not be observed.");
            if (!HasRoleAssignmentPermission(registryResourceRoles))
                return Failed(RbacInsufficientCode, "The managed Azure identity lacks the required registry-scope permissions.");

            return new AzureProviderAuthorityPreflightResult(
                true,
                "azure.preflight.succeeded",
                "The managed Azure identity and required provider permissions are available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Failed(ObservationInvalidCode, "The Azure authority observation shape is invalid.");
        }
        catch (Exception)
        {
            return Failed("azure.preflight.failed", "The managed Azure authority preflight could not complete.");
        }
    }

    private async Task<AzureProviderAuthorityPreflightResult?> RunNoOutputAsync(
        IReadOnlyList<string> arguments,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var result = await _process.ExecuteAsync(
            Request(arguments),
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? null : Failed(failureCode, failureMessage);
    }

    private async Task<PreflightAccount?> RunAccountObservationAsync(CancellationToken cancellationToken)
    {
        var result = await _process.ExecuteAsync(
            Request(["account", "show", "--subscription", _scope.SubscriptionId,
                "--query", "{id:id,principal:user.name}", "--output", "json", "--only-show-errors"]),
            ParseAccount,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? result.Value : null;
    }

    private async Task<bool?> RunBooleanObservationAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _process.ExecuteAsync(Request(arguments), ParseBoolean, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? result.Value?.Value : null;
    }

    private async Task<PreflightRoles?> RunRoleObservationAsync(
        string scope,
        string principalId,
        CancellationToken cancellationToken)
    {
        var result = await _process.ExecuteAsync(
            Request(["role", "assignment", "list", "--scope", scope, "--assignee", principalId,
                "--include-inherited", "--all", "--query", "[].roleDefinitionName", "--output", "json",
                "--only-show-errors"]),
            ParseRoles,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? result.Value : null;
    }

    private AzureCommandProcessRequest Request(IReadOnlyList<string> arguments) =>
        new(
            _options.AzureCliPath,
            arguments.Select(AzureCommandArgument.Safe).ToArray());

    private static PreflightAccount ParseAccount(ReadOnlyMemory<char> output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("id", out var idElement) ||
            !root.TryGetProperty("principal", out var principalElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            principalElement.ValueKind != JsonValueKind.String)
            throw new FormatException("The Azure account observation shape is invalid.");

        var subscriptionId = idElement.GetString();
        var principalId = principalElement.GetString();
        if (!IsCanonicalGuid(subscriptionId) || !IsCanonicalGuid(principalId))
            throw new FormatException("The Azure account observation identity is invalid.");
        return new(subscriptionId!, principalId!);
    }

    private static SafeBoolean ParseBoolean(ReadOnlyMemory<char> output)
    {
        var value = output.ToString().Trim();
        return bool.TryParse(value, out var parsed)
            ? new SafeBoolean(parsed)
            : throw new FormatException("The Azure group observation is invalid.");
    }

    private static PreflightRoles ParseRoles(ReadOnlyMemory<char> output)
    {
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("The Azure role observation shape is invalid.");

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new FormatException("The Azure role observation contains an invalid role.");
            var role = element.GetString();
            if (string.IsNullOrWhiteSpace(role) || role.Length > 128 || role.Any(char.IsControl))
                throw new FormatException("The Azure role observation contains an invalid role.");
            roles.Add(role);
        }
        return new(roles);
    }

    private static bool HasRequiredRoles(PreflightRoles observed) =>
        HasMutationRole(observed) && HasRoleAssignmentPermission(observed);

    private static bool HasMutationRole(PreflightRoles observed) => observed.Roles.Any(MutationRoles.Contains);

    private static bool HasRoleAssignmentPermission(PreflightRoles observed) =>
        observed.Roles.Any(RoleAssignmentRoles.Contains);

    private static bool IsCanonicalGuid(string? value) =>
        Guid.TryParseExact(value, "D", out _) &&
        string.Equals(value, value?.ToLowerInvariant(), StringComparison.Ordinal);

    private static string SubscriptionScope(string subscriptionId) => $"/subscriptions/{subscriptionId}";

    private static string ResourceGroupScope(string subscriptionId, string resourceGroupName) =>
        $"{SubscriptionScope(subscriptionId)}/resourceGroups/{resourceGroupName}";

    private static string RegistryScope(AzureProviderTargetScope scope) =>
        $"{ResourceGroupScope(scope.RegistrySubscriptionId, scope.RegistryResourceGroupName)}/providers/Microsoft.ContainerRegistry/registries/{scope.RegistryName}";

    private static AzureProviderAuthorityPreflightResult Failed(string code, string message) =>
        new(false, code, message);

    private sealed class PreflightAccount(string subscriptionId, string principalId) : AzureCommandSafeOutput
    {
        public string SubscriptionId { get; } = subscriptionId;
        public string PrincipalId { get; } = principalId;
    }

    private sealed class SafeBoolean(bool value) : AzureCommandSafeOutput
    {
        public bool Value { get; } = value;
    }

    private sealed class PreflightRoles(IReadOnlySet<string> roles) : AzureCommandSafeOutput
    {
        public IReadOnlySet<string> Roles { get; } = roles;
    }
}
