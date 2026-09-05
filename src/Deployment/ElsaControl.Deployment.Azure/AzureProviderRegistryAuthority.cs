using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Azure;

/// <summary>Explicitly selects the registry authority dialect used by the provider.</summary>
public enum AzureProviderRegistryAuthorityMode
{
    /// <summary>Existing deployments using stable built-in role definitions.</summary>
    BuiltIn = 0,

    /// <summary>Pinned custom deployment metadata and conditional registry RBAC authority.</summary>
    Narrow = 1
}

/// <summary>
/// Constants and validation for the shared-registry authority. The narrow profile is deliberately
/// closed over one reviewed role definition and one reviewed delegation condition; it is not a
/// general Azure RBAC policy evaluator.
/// </summary>
internal static class AzureProviderRegistryAuthority
{
    internal const string AcrPullRoleDefinitionId = "7f951dda-4ed3-4680-a7ca-43fe172d538d";
    internal const string ContributorRoleDefinitionId = "b24988ac-6180-42a0-ab88-20f7382dd24c";
    internal const string OwnerRoleDefinitionId = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635";
    internal const string UserAccessAdministratorRoleDefinitionId = "18d7d88d-d35e-4fb5-a5c3-7773c20a72d9";
    internal const string RbacAdministratorRoleDefinitionId = "f58310d9-a9f6-439a-9e8d-f62e7b41a168";

    internal const string RegistryRoleAdministrationConditionVersion = "2.0";
    internal const string RegistryRoleAdministrationCondition =
        "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalType] StringEqualsIgnoreCase 'ServicePrincipal')) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d} AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalType] StringEqualsIgnoreCase 'ServicePrincipal'))";

    internal static readonly IReadOnlySet<string> BuiltInMutationRoleDefinitionIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ContributorRoleDefinitionId,
            OwnerRoleDefinitionId
        };

    internal static readonly IReadOnlySet<string> BuiltInRoleAssignmentRoleDefinitionIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OwnerRoleDefinitionId,
            UserAccessAdministratorRoleDefinitionId,
            RbacAdministratorRoleDefinitionId
        };

    internal static readonly IReadOnlySet<string> NarrowMetadataActions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.Resources/deployments/read",
            "Microsoft.Resources/deployments/write",
            "Microsoft.Resources/deployments/delete",
            "Microsoft.Resources/deployments/cancel/action",
            "Microsoft.Resources/deployments/validate/action",
            "Microsoft.Resources/deployments/whatIf/action",
            "Microsoft.Resources/deployments/exportTemplate/action",
            "Microsoft.Resources/deployments/operations/read",
            "Microsoft.Resources/deployments/operationstatuses/read",
            "Microsoft.Resources/subscriptions/resourceGroups/read",
            "Microsoft.ContainerRegistry/registries/read",
            "Microsoft.Authorization/roleAssignments/read",
            "Microsoft.Authorization/roleDefinitions/read"
        };

    internal static void ValidateConfiguration(
        AzureProviderRegistryAuthorityMode mode,
        string? roleDefinitionId,
        string? roleAssignmentId,
        string? roleAdministrationAssignmentId)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException("The Azure registry authority mode is invalid.", nameof(mode));

        if (mode == AzureProviderRegistryAuthorityMode.BuiltIn)
        {
            if (roleDefinitionId is not null || roleAssignmentId is not null || roleAdministrationAssignmentId is not null)
                throw new ArgumentException("Pinned registry authority IDs require the narrow registry authority mode.");
            return;
        }

        ValidateRoleDefinitionIdSyntax(roleDefinitionId, "registry deployment metadata role definition ID");
        ValidateRoleAssignmentIdSyntax(roleAssignmentId, "registry deployment metadata role assignment ID");
        ValidateRoleAssignmentIdSyntax(roleAdministrationAssignmentId, "registry role administration assignment ID");
    }

    internal static void ValidateForScope(
        AzureProviderTargetScope scope,
        AzureProviderRegistryAuthorityMode mode,
        string? roleDefinitionId,
        string? roleAssignmentId,
        string? roleAdministrationAssignmentId)
    {
        ValidateConfiguration(mode, roleDefinitionId, roleAssignmentId, roleAdministrationAssignmentId);
        if (mode == AzureProviderRegistryAuthorityMode.BuiltIn)
            return;

        var registryGroupScope = ResourceGroupScope(scope.RegistrySubscriptionId, scope.RegistryResourceGroupName);
        var registryScope = RegistryScope(scope);
        if (!IsExactRoleDefinitionId(roleDefinitionId!, $"/subscriptions/{scope.RegistrySubscriptionId}") ||
            !IsExactRoleAssignmentId(roleAssignmentId!, registryGroupScope) ||
            !IsExactRoleAssignmentId(roleAdministrationAssignmentId!, registryScope))
            throw new ArgumentException("The pinned Azure registry authority IDs are outside the governed registry scopes.");
    }

    internal static string RoleDefinitionResourceId(string roleDefinitionId) =>
        roleDefinitionId.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

    internal static bool IsRoleDefinitionId(string? value, string expectedId)
    {
        if (string.IsNullOrWhiteSpace(value) || !TryGetResourceGuid(value, "roleDefinitions", out var id))
            return false;
        return string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsExactRoleDefinitionId(string value, string expectedScope)
    {
        if (!TryGetRolePath(value, "roleDefinitions", out var scope, out _))
            return false;
        return string.Equals(scope, expectedScope, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsExactRoleAssignmentId(string value, string expectedScope)
    {
        if (!TryGetRolePath(value, "roleAssignments", out var scope, out _))
            return false;
        return string.Equals(scope, expectedScope, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsCanonicalRoleAssignmentId(string? value) =>
        value is not null && TryGetResourceGuid(value, "roleAssignments", out _);

    internal static string NormalizeCondition(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => char.IsControl(character) && character is not '\r' and not '\n'))
            return "";

        var normalized = new System.Text.StringBuilder(value.Length);
        var inLiteral = false;
        foreach (var character in value)
        {
            if (character == '\'')
            {
                inLiteral = !inLiteral;
                normalized.Append(character);
            }
            else if (inLiteral && (character is '\r' or '\n'))
                return "";
            else if (inLiteral || !char.IsWhiteSpace(character))
                normalized.Append(inLiteral ? character : char.ToLowerInvariant(character));
        }

        return inLiteral ? "" : normalized.ToString();
    }

    internal static string ResourceGroupScope(string subscriptionId, string resourceGroupName) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}";

    internal static string RegistryScope(AzureProviderTargetScope scope) =>
        $"{ResourceGroupScope(scope.RegistrySubscriptionId, scope.RegistryResourceGroupName)}/providers/Microsoft.ContainerRegistry/registries/{scope.RegistryName}";

    private static void ValidateRoleDefinitionIdSyntax(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            !TryGetResourceGuid(value, "roleDefinitions", out _))
            throw new ArgumentException("The pinned Azure registry authority ID is invalid.", name);
    }

    private static void ValidateRoleAssignmentIdSyntax(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            !TryGetResourceGuid(value, "roleAssignments", out _))
            throw new ArgumentException("The pinned Azure registry authority ID is invalid.", name);
    }

    private static bool TryGetRolePath(string value, string resourceType, out string scope, out string id)
    {
        scope = "";
        id = "";
        if (!TryGetResourceGuid(value, resourceType, out id))
            return false;

        var marker = $"/providers/Microsoft.Authorization/{resourceType}/";
        var index = value.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0 || index + marker.Length + id.Length != value.Length)
            return false;
        scope = value[..index];
        return IsSafeArmPath(scope);
    }

    private static bool TryGetResourceGuid(string value, string resourceType, out string id)
    {
        id = "";
        if (!IsSafeArmPath(value))
            return false;
        var marker = $"/providers/Microsoft.Authorization/{resourceType}/";
        var index = value.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;
        var candidate = value[(index + marker.Length)..];
        if (!Guid.TryParseExact(candidate, "D", out var guid) || guid == Guid.Empty ||
            !string.Equals(candidate, guid.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains('/', StringComparison.Ordinal))
            return false;

        id = candidate;
        return true;
    }

    private static bool IsSafeArmPath(string value) =>
        value.Length is > 0 and <= 2048 && value[0] == '/' && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) &&
        !Regex.IsMatch(value, @"//|[?#\\]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
