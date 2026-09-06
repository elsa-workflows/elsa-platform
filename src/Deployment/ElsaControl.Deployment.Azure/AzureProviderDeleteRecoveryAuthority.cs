using System.Globalization;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Immutable, value-free authority captured when an explicit lifecycle Delete recovery is
/// accepted. It binds the recovery to one persisted provider operation version; callers must not
/// reconstruct this value from the latest provider operation at execution time.
/// </summary>
public sealed record AzureProviderDeleteRecoveryAuthority(
    Guid ProviderOperationId,
    Guid ProviderAssignmentId,
    int LifecycleAttemptNumber,
    int InstanceVersion,
    int ProviderAttemptNumber,
    long ProviderVersion,
    long ProviderCheckpointSequence,
    string ProviderOperationIdentity,
    string ProviderRequestHash,
    string TargetKey,
    string ProviderScopeFingerprint,
    string ProviderPlanFingerprint,
    string ProviderTemplateFingerprint)
{
    private const string Version = "v1";
    private const char Separator = '|';

    public string Serialize()
    {
        Validate();
        return string.Join(Separator,
            Version,
            ProviderOperationId.ToString("N"),
            ProviderAssignmentId.ToString("N"),
            LifecycleAttemptNumber.ToString(CultureInfo.InvariantCulture),
            InstanceVersion.ToString(CultureInfo.InvariantCulture),
            ProviderAttemptNumber.ToString(CultureInfo.InvariantCulture),
            ProviderVersion.ToString(CultureInfo.InvariantCulture),
            ProviderCheckpointSequence.ToString(CultureInfo.InvariantCulture),
            ProviderOperationIdentity,
            ProviderRequestHash,
            TargetKey,
            ProviderScopeFingerprint,
            ProviderPlanFingerprint,
            ProviderTemplateFingerprint);
    }

    public void Validate()
    {
        if (ProviderOperationId == Guid.Empty || ProviderAssignmentId == Guid.Empty ||
            LifecycleAttemptNumber < 2 || InstanceVersion < 1 || ProviderAttemptNumber < 1 ||
            ProviderVersion < 1 || ProviderCheckpointSequence < 1 ||
            !IsFingerprint(ProviderOperationIdentity) || !IsFingerprint(ProviderRequestHash) ||
            !IsSafeTargetKey(TargetKey) || !IsFingerprint(ProviderScopeFingerprint) ||
            !IsFingerprint(ProviderPlanFingerprint) || !IsFingerprint(ProviderTemplateFingerprint))
            throw new ArgumentException("Azure delete recovery authority is invalid.");
    }

    public static bool TryParse(string? serialized, out AzureProviderDeleteRecoveryAuthority? authority)
    {
        authority = null;
        if (string.IsNullOrWhiteSpace(serialized) || serialized.Length > 4096)
            return false;

        var parts = serialized.Split(Separator);
        if (parts.Length != 14 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
            return false;
        if (!Guid.TryParseExact(parts[1], "N", out var providerOperationId) || providerOperationId == Guid.Empty ||
            !Guid.TryParseExact(parts[2], "N", out var providerAssignmentId) || providerAssignmentId == Guid.Empty ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var lifecycleAttemptNumber) ||
            !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var instanceVersion) ||
            !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var providerAttemptNumber) ||
            !long.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out var providerVersion) ||
            !long.TryParse(parts[7], NumberStyles.None, CultureInfo.InvariantCulture, out var checkpointSequence))
            return false;

        try
        {
            var candidate = new AzureProviderDeleteRecoveryAuthority(
                providerOperationId,
                providerAssignmentId,
                lifecycleAttemptNumber,
                instanceVersion,
                providerAttemptNumber,
                providerVersion,
                checkpointSequence,
                parts[8],
                parts[9],
                parts[10],
                parts[11],
                parts[12],
                parts[13]);
            candidate.Validate();
            if (!string.Equals(candidate.Serialize(), serialized, StringComparison.Ordinal))
                return false;
            authority = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && !value.AsSpan().ContainsAnyExcept("0123456789abcdef");

    private static bool IsSafeTargetKey(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');
}

/// <summary>
/// Caller-bound inputs for the atomic Delete recovery claim. The durable recovery row remains
/// authoritative; these values only identify the currently leased lifecycle work item.
/// </summary>
public sealed record AzureProviderDeleteRecoveryClaimRequest(
    Guid RecoveryRequestId,
    Guid WorkspaceId,
    Guid InstanceId,
    Guid LifecycleOperationId,
    int LifecycleAttemptNumber,
    int InstanceVersion,
    string WorkerId,
    string LeaseToken,
    int LeaseVersion)
{
    public void Validate()
    {
        if (RecoveryRequestId == Guid.Empty || WorkspaceId == Guid.Empty || InstanceId == Guid.Empty ||
            LifecycleOperationId == Guid.Empty || LifecycleAttemptNumber < 2 || InstanceVersion < 1 ||
            LeaseVersion < 1)
            throw new ArgumentException("Azure delete recovery claim identity is invalid.");
        AzureProviderOperationValidation.ValidateWorkerId(WorkerId);
        AzureProviderOperationValidation.ValidateLeaseToken(LeaseToken);
    }
}

/// <summary>
/// Optional persistence capability for the explicit Azure Delete recovery path. It is separate
/// from the ordinary operation store so lightweight provider fakes and local Delete providers do
/// not acquire an implicit RecoveryRequired replay capability.
/// </summary>
public interface IAzureProviderDeleteRecoveryStore
{
    Task<AzureProviderDeleteRecoveryAuthority?> GetDeleteRecoveryAuthorityAsync(
        Guid workspaceId,
        Guid recoveryRequestId,
        Guid instanceId,
        Guid lifecycleOperationId,
        CancellationToken cancellationToken = default);

    Task<AzureProviderOperation?> ClaimDeleteRecoveryAsync(
        AzureProviderDeleteRecoveryClaimRequest request,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
