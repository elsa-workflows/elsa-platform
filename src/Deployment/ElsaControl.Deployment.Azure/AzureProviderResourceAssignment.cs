namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Provider-owned lifecycle for the durable Azure placement assigned to one Elsa Instance.
/// The control-plane instance retains only this assignment's opaque identifier.
/// </summary>
public enum AzureProviderAssignmentState
{
    Reserved,
    Provisioning,
    Active,
    Deleting,
    Unknown,
    Deleted
}

/// <summary>
/// Durable provider placement and ownership authority. Provider resource identities never cross
/// into the provider-neutral Elsa Instance contract.
/// </summary>
public sealed record AzureProviderResourceAssignment(
    Guid Id,
    Guid WorkspaceId,
    Guid OrganizationId,
    Guid InstanceId,
    string ProviderScopeFingerprint,
    int NamingVersion,
    string SubscriptionId,
    string ResourceGroupName,
    string WorkloadName,
    string OwnershipKey,
    string Location,
    AzureProviderAssignmentState State,
    AzureProviderResourceReferences Resources,
    Guid? LastOperationId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt = null);

public sealed record AzureProviderResourceAssignmentRequest(
    Guid WorkspaceId,
    Guid OrganizationId,
    Guid InstanceId,
    string ProviderScopeFingerprint,
    string SubscriptionId,
    string ResourceGroupNamePrefix,
    string WorkloadName,
    string Location,
    int NamingVersion = AzureProviderResourceAssignmentNaming.CurrentVersion);

public interface IAzureProviderResourceAssignmentStore
{
    Task<AzureProviderResourceAssignment> CreateOrGetAsync(
        AzureProviderResourceAssignmentRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<AzureProviderResourceAssignment?> GetAsync(
        Guid workspaceId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);
}

public static class AzureProviderResourceAssignmentNaming
{
    /// <summary>Legacy proof hosts bind the exact caller-supplied disposable group.</summary>
    public const int ExplicitDisposableGroup = 0;

    public const int CurrentVersion = 1;

    public static string ResourceGroupName(string prefix, Guid instanceId, int namingVersion = CurrentVersion)
    {
        if (namingVersion == ExplicitDisposableGroup && instanceId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(prefix) && prefix.Length <= 90 &&
            prefix.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '(' or ')' or '-'))
            return prefix;
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 50 ||
            prefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("The Azure resource-group prefix is unsafe.", nameof(prefix));
        if (instanceId == Guid.Empty || namingVersion != CurrentVersion)
            throw new ArgumentException("The Azure assignment naming authority is invalid.", nameof(instanceId));

        return $"{prefix.TrimEnd('-')}-{instanceId:N}";
    }

    public static string OwnershipKey(Guid assignmentId, Guid instanceId, string providerScopeFingerprint)
    {
        if (assignmentId == Guid.Empty || instanceId == Guid.Empty ||
            providerScopeFingerprint is not { Length: 64 } || !providerScopeFingerprint.All(char.IsAsciiHexDigit))
            throw new ArgumentException("The Azure assignment ownership identity is invalid.");

        var value = $"{assignmentId:D}:{instanceId:D}:{providerScopeFingerprint.ToLowerInvariant()}";
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
