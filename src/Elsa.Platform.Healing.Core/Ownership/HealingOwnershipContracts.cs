using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.Core.Ownership;

public static class HealingOwnershipReasonCodes
{
    public const string Succeeded = "succeeded";
    public const string Unauthorized = "unauthorized";
    public const string OwnerApprovalRequired = "owner-approval-required";
    public const string AutomaticMergePermissionRequired = "automerge-permission-required";
    public const string NotFound = "not-found";
    public const string InvalidConfiguration = "invalid-configuration";
    public const string InvalidManifest = "invalid-manifest";
    public const string IdempotencyConflict = "idempotency-key-conflict";
    public const string ImmutableRevisionConflict = "immutable-revision-conflict";
    public const string AdministrationConflict = "administration-conflict";
    public const string InvalidTrustTransition = "invalid-trust-transition";
    public const string TrustedAttestationRequired = "trusted-attestation-required";
    public const string AttestationRejected = "attestation-rejected";
    public const string InvalidBindingTransition = "invalid-binding-transition";
    public const string ProviderNotAuthorized = "provider-not-authorized";
    public const string ProviderValidationFailed = "provider-validation-failed";
    public const string ProviderValidationUnavailable = "provider-validation-unavailable";
    public const string ProviderRepositoryMismatch = "provider-repository-mismatch";
    public const string PolicyNotTrusted = "policy-not-trusted";
    public const string AmbiguousAuthority = "ambiguous-authority";
    public const string NoApprovedBinding = "no-approved-binding";
    public const string ManifestNotTrusted = "manifest-not-trusted";
}

public sealed record HealingAuthorization(
    Guid WorkspaceId,
    Guid ApplicationId,
    string ActorId,
    bool IsWorkspaceOwner,
    IReadOnlySet<string> Permissions)
{
    public bool Allows(Guid workspaceId, Guid applicationId, string permission) =>
        WorkspaceId == workspaceId &&
        ApplicationId == applicationId &&
        !string.IsNullOrWhiteSpace(ActorId) &&
        Permissions.Contains(permission);
}

public sealed record HealingOperationResult<T>(bool Succeeded, string ReasonCode, T? Value = default)
{
    public static HealingOperationResult<T> Success(T value) =>
        new(true, HealingOwnershipReasonCodes.Succeeded, value);

    public static HealingOperationResult<T> Denied(string reasonCode) => new(false, reasonCode);
}

internal static class HealingOwnershipAuthorization
{
    public static string? ConfigurationFailure(
        HealingAuthorization authorization,
        Guid workspaceId,
        Guid applicationId,
        bool automaticMergeRequested = false)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (!authorization.Allows(workspaceId, applicationId, HealingPermissions.Configure))
            return HealingOwnershipReasonCodes.Unauthorized;
        if (automaticMergeRequested && !authorization.Permissions.Contains(HealingPermissions.ConfigureAutoMerge))
            return HealingOwnershipReasonCodes.AutomaticMergePermissionRequired;
        return null;
    }

    public static string? OwnerFailure(
        HealingAuthorization authorization,
        Guid workspaceId,
        Guid applicationId,
        bool automaticMergeRequested = false)
    {
        var configurationFailure = ConfigurationFailure(
            authorization, workspaceId, applicationId, automaticMergeRequested);
        return configurationFailure ?? (authorization.IsWorkspaceOwner
            ? null
            : HealingOwnershipReasonCodes.OwnerApprovalRequired);
    }

    public static string? ReadFailure(
        HealingAuthorization authorization,
        Guid workspaceId,
        Guid applicationId)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return authorization.WorkspaceId == workspaceId &&
               authorization.ApplicationId == applicationId &&
               !string.IsNullOrWhiteSpace(authorization.ActorId) &&
               (authorization.Permissions.Contains(HealingPermissions.Read) ||
                authorization.Permissions.Contains(HealingPermissions.Configure))
            ? null
            : HealingOwnershipReasonCodes.Unauthorized;
    }
}
