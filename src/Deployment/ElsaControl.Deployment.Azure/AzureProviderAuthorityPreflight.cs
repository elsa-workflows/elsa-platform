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
        _options.ValidateRegistryAuthority(_scope);
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
                _options.ValidateRegistryAuthority(_scope);
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
                !string.Equals(account.ClientId, _options.AzureCliClientId, StringComparison.Ordinal))
                return Failed(ObservationInvalidCode, "The authenticated Azure identity did not match the configured authority.");

            // Resolve the ARM identity's object ID without requiring Microsoft Graph access.
            // The bounded provider profile hosts its identity in the workload subscription.
            var principal = await _process.ExecuteAsync(
                Request(["identity", "list", "--subscription", _scope.SubscriptionId,
                    "--query", $"[?clientId=='{_options.AzureCliClientId}'].principalId", "--output", "json", "--only-show-errors"]),
                ParsePrincipal,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!principal.Succeeded || principal.Value is null ||
                principal.Value.Value != _options.SqlBootstrapObjectId)
                return Failed(ObservationInvalidCode, "The managed Azure identity does not match the configured bootstrap authority.");
            var principalId = principal.Value.Value;

            var targetGroupExists = await RunBooleanObservationAsync(
                ["group", "exists", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName,
                    "--output", "tsv", "--only-show-errors"],
                cancellationToken);
            if (targetGroupExists is null)
                return Failed(ObservationFailureCode, "The configured Azure target scope could not be observed.");

            if (!targetGroupExists.Value)
                return Failed(ObservationInvalidCode, "The governed Azure target scope does not exist.");

            // Each managed instance receives a new sibling resource group. Authority on the
            // configured anchor group alone cannot create or administer those groups.
            var targetScope = $"/subscriptions/{_scope.SubscriptionId}";
            var targetRoles = await RunRoleObservationAsync(targetScope, principalId, cancellationToken);
            if (targetRoles is null)
                return Failed(ObservationFailureCode, "The configured Azure target permissions could not be observed.");
            if (!HasRequiredRoles(targetRoles))
                return Failed(RbacInsufficientCode, "The managed Azure identity lacks the required target-scope permissions.");

            // The registry phase submits a resource-group deployment, then creates a role
            // assignment at the registry resource scope. Check both scopes: a Contributor role
            // assigned only to the registry resource cannot submit the group deployment, while a
            // role-assignment role assigned only to the group does not authorize the child scope.
            if (_options.RegistryAuthorityMode == AzureProviderRegistryAuthorityMode.Narrow)
            {
                var definition = await RunRoleDefinitionObservationAsync(cancellationToken);
                if (definition is null)
                    return Failed(ObservationFailureCode, "The configured Azure registry role definition could not be observed.");
                if (!MatchesNarrowMetadataRole(definition))
                    return Failed(RbacInsufficientCode, "The configured Azure registry metadata role is not the reviewed role.");

                var metadataAssignment = await RunRoleAssignmentObservationAsync(
                    AzureProviderRegistryAuthority.ResourceGroupScope(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName),
                    principalId,
                    cancellationToken);
                if (metadataAssignment is null)
                    return Failed(ObservationFailureCode, "The configured Azure registry metadata assignment could not be observed.");
                if (!HasExactMetadataAssignment(metadataAssignment, principalId))
                    return Failed(RbacInsufficientCode, "The managed Azure identity lacks the configured registry metadata authority.");

                var registryAssignment = await RunRoleAssignmentObservationAsync(
                    AzureProviderRegistryAuthority.RegistryScope(_scope),
                    principalId,
                    cancellationToken);
                if (registryAssignment is null)
                    return Failed(ObservationFailureCode, "The configured Azure registry administration assignment could not be observed.");
                if (!HasExactRegistryAdministrationAssignment(registryAssignment, principalId))
                    return Failed(RbacInsufficientCode, "The managed Azure identity lacks the configured registry administration authority.");
            }
            else
            {
                var registryGroupRoles = await RunRoleObservationAsync(
                    AzureProviderRegistryAuthority.ResourceGroupScope(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName),
                    principalId,
                    cancellationToken);
                if (registryGroupRoles is null)
                    return Failed(ObservationFailureCode, "The configured Azure registry resource group permissions could not be observed.");
                if (!HasMutationRole(registryGroupRoles))
                    return Failed(RbacInsufficientCode, "The managed Azure identity lacks registry deployment permissions.");

                var registryResourceRoles = await RunRoleObservationAsync(AzureProviderRegistryAuthority.RegistryScope(_scope), principalId, cancellationToken);
                if (registryResourceRoles is null)
                    return Failed(ObservationFailureCode, "The configured Azure registry role-assignment permissions could not be observed.");
                if (!HasRoleAssignmentPermission(registryResourceRoles))
                    return Failed(RbacInsufficientCode, "The managed Azure identity lacks the required registry-scope permissions.");
            }

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
                "--query", "{id:id,name:user.name,type:user.type,identity:user.assignedIdentityInfo}", "--output", "json", "--only-show-errors"]),
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
            Request(["role", "assignment", "list", "--scope", scope, "--assignee-object-id", principalId, "--fill-principal-name", "false",
                "--fill-role-definition-name", "false", "--include-inherited", "--query", "[].roleDefinitionId", "--output", "json",
                "--only-show-errors"]),
            ParseRoles,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? result.Value : null;
    }

    private async Task<PreflightRoleAssignments?> RunRoleAssignmentObservationAsync(
        string scope,
        string principalId,
        CancellationToken cancellationToken)
    {
        var result = await _process.ExecuteAsync(
            Request(["role", "assignment", "list", "--scope", scope, "--assignee-object-id", principalId,
                "--fill-principal-name", "false", "--fill-role-definition-name", "false", "--include-inherited",
                "--query", "[].{id:id,scope:scope,principalId:principalId,principalType:principalType,roleDefinitionId:roleDefinitionId,condition:condition,conditionVersion:conditionVersion}",
                "--output", "json", "--only-show-errors"]),
            ParseRoleAssignments,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded ? result.Value : null;
    }

    private async Task<PreflightRoleDefinition?> RunRoleDefinitionObservationAsync(CancellationToken cancellationToken)
    {
        var roleDefinitionId = AzureProviderRegistryAuthority.RoleDefinitionResourceId(_options.RegistryDeploymentMetadataRoleDefinitionId!);
        var url = $"https://management.azure.com/subscriptions/{_scope.RegistrySubscriptionId}/resourceGroups/{_scope.RegistryResourceGroupName}/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionId}?api-version=2022-04-01";
        var result = await _process.ExecuteAsync(
            Request(["rest", "--method", "get", "--url", url,
                "--query", "{id:id,type:properties.type,permissions:properties.permissions,assignableScopes:properties.assignableScopes}",
                "--output", "json", "--only-show-errors"]),
            ParseRoleDefinition,
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
            !root.TryGetProperty("name", out var nameElement) ||
            !root.TryGetProperty("type", out var typeElement) ||
            !root.TryGetProperty("identity", out var identityElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            nameElement.ValueKind != JsonValueKind.String ||
            typeElement.ValueKind != JsonValueKind.String ||
            identityElement.ValueKind != JsonValueKind.String)
            throw new FormatException("The Azure account observation shape is invalid.");

        var subscriptionId = idElement.GetString();
        // Azure CLI 2.77 stores --client-id logins as userAssignedIdentity plus
        // assignedIdentityInfo = MSIClient-<client ID>, not a GUID in user.name.
        const string prefix = "MSIClient-";
        var identity = identityElement.GetString();
        if (nameElement.GetString() != "userAssignedIdentity" || typeElement.GetString() != "servicePrincipal" ||
            identity is null || !identity.StartsWith(prefix, StringComparison.Ordinal))
            throw new FormatException("The Azure account is not the configured managed identity login kind.");
        var clientId = identity[prefix.Length..];
        if (!IsCanonicalGuid(subscriptionId) || !IsCanonicalGuid(clientId))
            throw new FormatException("The Azure account observation identity is invalid.");
        return new(subscriptionId!, clientId!);
    }

    private static SafeBoolean ParseBoolean(ReadOnlyMemory<char> output)
    {
        var value = output.ToString().Trim();
        return bool.TryParse(value, out var parsed)
            ? new SafeBoolean(parsed)
            : throw new FormatException("The Azure group observation is invalid.");
    }

    private static SafePrincipal ParsePrincipal(ReadOnlyMemory<char> output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1 ||
            root[0].ValueKind != JsonValueKind.String || !IsCanonicalGuid(root[0].GetString()))
            throw new FormatException("The managed identity object observation is invalid.");
        return new(root[0].GetString()!);
    }

    private sealed class SafePrincipal(string value) : AzureCommandSafeOutput
    {
        public string Value { get; } = value;
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
            if (string.IsNullOrWhiteSpace(role) || role.Length > 2048 || role.Any(char.IsControl) ||
                !TryGetRoleDefinitionGuid(role, out var roleDefinitionId))
                throw new FormatException("The Azure role observation contains an invalid role.");
            roles.Add(roleDefinitionId);
        }
        return new(roles);
    }

    private static PreflightRoleAssignments ParseRoleAssignments(ReadOnlyMemory<char> output)
    {
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("The Azure role assignment observation shape is invalid.");

        var assignments = new List<PreflightRoleAssignment>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !ReadRequiredString(element, "id", out var id) ||
                !ReadRequiredString(element, "scope", out var scope) ||
                !ReadRequiredString(element, "principalId", out var principalId) ||
                !ReadRequiredString(element, "principalType", out var principalType) ||
                !ReadRequiredString(element, "roleDefinitionId", out var roleDefinitionId) ||
                !AzureProviderRegistryAuthority.IsCanonicalRoleAssignmentId(id) ||
                !IsCanonicalArmPath(scope) ||
                !IsCanonicalGuid(principalId) ||
                !string.Equals(principalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase) ||
                !IsCanonicalArmPath(roleDefinitionId) ||
                !TryGetRoleDefinitionGuid(roleDefinitionId, out _))
                throw new FormatException("The Azure role assignment observation contains an invalid assignment.");

            var condition = ReadOptionalString(element, "condition");
            var conditionVersion = ReadOptionalString(element, "conditionVersion");
            if (condition is not null && condition.Length > 4096 || conditionVersion is not null && conditionVersion.Length > 32)
                throw new FormatException("The Azure role assignment observation contains an invalid condition.");
            if (!ids.Add(id))
                throw new FormatException("The Azure role assignment observation contains a duplicate assignment.");
            assignments.Add(new(id, scope, principalId, principalType, roleDefinitionId, condition, conditionVersion));
        }
        return new(assignments);
    }

    private static PreflightRoleDefinition ParseRoleDefinition(ReadOnlyMemory<char> output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !ReadRequiredString(root, "id", out var id) ||
            !ReadRequiredString(root, "type", out var type) ||
            !IsCanonicalArmPath(id) ||
            !string.Equals(type, "CustomRole", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("assignableScopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array ||
            scopes.GetArrayLength() != 1 || scopes[0].ValueKind != JsonValueKind.String ||
            !IsCanonicalArmPath(scopes[0].GetString()))
            throw new FormatException("The Azure role definition observation shape is invalid.");

        if (!root.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array ||
            permissions.GetArrayLength() != 1)
            throw new FormatException("The Azure role definition permissions are invalid.");

        var permission = permissions[0];
        if (permission.ValueKind != JsonValueKind.Object ||
            !ReadStringSet(permission, "actions", out var actions) ||
            !ReadStringSet(permission, "notActions", out var notActions) ||
            !ReadStringSet(permission, "dataActions", out var dataActions) ||
            !ReadStringSet(permission, "notDataActions", out var notDataActions))
            throw new FormatException("The Azure role definition permissions are invalid.");

        return new(id, scopes[0].GetString()!, actions, notActions, dataActions, notDataActions);
    }

    private bool MatchesNarrowMetadataRole(PreflightRoleDefinition definition) =>
        string.Equals(definition.Id, _options.RegistryDeploymentMetadataRoleDefinitionId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(definition.AssignableScope, AzureProviderRegistryAuthority.ResourceGroupScope(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName), StringComparison.OrdinalIgnoreCase) &&
        definition.Actions.SetEquals(AzureProviderRegistryAuthority.NarrowMetadataActions) &&
        definition.NotActions.Count == 0 && definition.DataActions.Count == 0 && definition.NotDataActions.Count == 0;

    private bool HasExactMetadataAssignment(PreflightRoleAssignments observed, string principalId) =>
        observed.Assignments.Count(assignment =>
            string.Equals(assignment.Id, _options.RegistryDeploymentMetadataRoleAssignmentId, StringComparison.OrdinalIgnoreCase)) == 1 &&
        observed.Assignments.Any(assignment =>
            string.Equals(assignment.Id, _options.RegistryDeploymentMetadataRoleAssignmentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.Scope, AzureProviderRegistryAuthority.ResourceGroupScope(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.PrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.RoleDefinitionId, _options.RegistryDeploymentMetadataRoleDefinitionId, StringComparison.OrdinalIgnoreCase) &&
            assignment.Condition is null && assignment.ConditionVersion is null);

    private bool HasExactRegistryAdministrationAssignment(PreflightRoleAssignments observed, string principalId) =>
        observed.Assignments.Count(assignment =>
            string.Equals(assignment.Id, _options.RegistryRoleAdministrationAssignmentId, StringComparison.OrdinalIgnoreCase)) == 1 &&
        observed.Assignments.Any(assignment =>
            string.Equals(assignment.Id, _options.RegistryRoleAdministrationAssignmentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.Scope, AzureProviderRegistryAuthority.RegistryScope(_scope), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.PrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                assignment.RoleDefinitionId,
                $"/subscriptions/{_scope.RegistrySubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{AzureProviderRegistryAuthority.RbacAdministratorRoleDefinitionId}",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.ConditionVersion, AzureProviderRegistryAuthority.RegistryRoleAdministrationConditionVersion, StringComparison.Ordinal) &&
            assignment.Condition is not null &&
            string.Equals(
                AzureProviderRegistryAuthority.NormalizeCondition(assignment.Condition),
                AzureProviderRegistryAuthority.NormalizeCondition(AzureProviderRegistryAuthority.RegistryRoleAdministrationCondition),
                StringComparison.Ordinal));

    private static bool ReadRequiredString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        var candidate = property.GetString();
        if (candidate is null || string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsControl))
            return false;

        value = candidate;
        return true;
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new FormatException("The Azure role assignment condition is invalid.");
        var value = property.GetString();
        return value is null || value.Any(character => char.IsControl(character) && character is not '\r' and not '\n')
            ? throw new FormatException("The Azure role assignment condition is invalid.")
            : value;
    }

    private static bool ReadStringSet(JsonElement element, string name, out IReadOnlySet<string> values)
    {
        values = new HashSet<string>(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) || item.GetString()!.Any(char.IsControl))
                return false;
            if (!((HashSet<string>)values).Add(item.GetString()!))
                return false;
        }
        return true;
    }

    private static bool TryGetRoleDefinitionGuid(string value, out string roleDefinitionId)
    {
        roleDefinitionId = "";
        if (!IsCanonicalArmPath(value))
            return false;
        var marker = "/providers/Microsoft.Authorization/roleDefinitions/";
        var index = value.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;
        var candidate = value[(index + marker.Length)..];
        if (!Guid.TryParseExact(candidate, "D", out var guid) || guid == Guid.Empty || candidate.Contains('/', StringComparison.Ordinal))
            return false;
        roleDefinitionId = guid.ToString("D");
        return true;
    }

    private static bool IsCanonicalArmPath(string? value) =>
        value is not null && value.Length is > 0 and <= 2048 && value[0] == '/' &&
        !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) &&
        !value.Contains("//", StringComparison.Ordinal) && !value.Contains('?', StringComparison.Ordinal) &&
        !value.Contains('#', StringComparison.Ordinal) && !value.Contains('\\', StringComparison.Ordinal);

    private static bool HasRequiredRoles(PreflightRoles observed) =>
        HasMutationRole(observed) && HasRoleAssignmentPermission(observed);

    private static bool HasMutationRole(PreflightRoles observed) => observed.Roles.Any(AzureProviderRegistryAuthority.BuiltInMutationRoleDefinitionIds.Contains);

    private static bool HasRoleAssignmentPermission(PreflightRoles observed) =>
        observed.Roles.Any(AzureProviderRegistryAuthority.BuiltInRoleAssignmentRoleDefinitionIds.Contains);

    private static bool IsCanonicalGuid(string? value) =>
        Guid.TryParseExact(value, "D", out _) &&
        string.Equals(value, value?.ToLowerInvariant(), StringComparison.Ordinal);

    private static AzureProviderAuthorityPreflightResult Failed(string code, string message) =>
        new(false, code, message);

    private sealed class PreflightAccount(string subscriptionId, string clientId) : AzureCommandSafeOutput
    {
        public string SubscriptionId { get; } = subscriptionId;
        public string ClientId { get; } = clientId;
    }

    private sealed class SafeBoolean(bool value) : AzureCommandSafeOutput
    {
        public bool Value { get; } = value;
    }

    private sealed class PreflightRoles(IReadOnlySet<string> roles) : AzureCommandSafeOutput
    {
        public IReadOnlySet<string> Roles { get; } = roles;
    }

    private sealed class PreflightRoleAssignments(IReadOnlyList<PreflightRoleAssignment> assignments) : AzureCommandSafeOutput
    {
        public IReadOnlyList<PreflightRoleAssignment> Assignments { get; } = assignments;
    }

    private sealed record PreflightRoleAssignment(
        string Id,
        string Scope,
        string PrincipalId,
        string PrincipalType,
        string RoleDefinitionId,
        string? Condition,
        string? ConditionVersion);

    private sealed class PreflightRoleDefinition(
        string id,
        string assignableScope,
        IReadOnlySet<string> actions,
        IReadOnlySet<string> notActions,
        IReadOnlySet<string> dataActions,
        IReadOnlySet<string> notDataActions) : AzureCommandSafeOutput
    {
        public string Id { get; } = id;
        public string AssignableScope { get; } = assignableScope;
        public IReadOnlySet<string> Actions { get; } = actions;
        public IReadOnlySet<string> NotActions { get; } = notActions;
        public IReadOnlySet<string> DataActions { get; } = dataActions;
        public IReadOnlySet<string> NotDataActions { get; } = notDataActions;
    }
}
