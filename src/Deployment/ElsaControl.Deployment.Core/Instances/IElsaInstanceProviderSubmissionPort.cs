using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Provider-neutral execution seam for an already-resolved lifecycle operation.
/// Implementations must make submission durable and idempotent using the operation
/// identity; a retry may call this method again but must not create a second remote
/// mutation. No request payload, credential or provider resource identifier belongs
/// in this contract.
/// </summary>
public interface IElsaInstanceProviderSubmissionPort
{
    Task<ElsaInstanceProviderSubmissionResult> SubmitAsync(
        ElsaInstanceProviderSubmission request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider seam for an already accepted recovery envelope. Recovery is separate
/// from ordinary submission so an attempt cannot be re-resolved or accidentally
/// create a second provider operation. Implementations must observe first and keep
/// provider mutation behind their durable recovery claim.
/// </summary>
public interface IElsaInstanceProviderRecoveryPort
{
    Task<ElsaInstanceProviderRecoveryResult> RecoverAsync(
        ElsaInstanceProviderRecoveryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ElsaInstanceProviderRecoveryRequest(
    ElsaInstanceProviderSubmission Submission,
    ElsaInstanceProviderRecoveryEnvelope? Envelope = null)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Submission);
        Submission.Validate();
        if (Submission.AttemptNumber < 2)
            throw new InvalidOperationException("Provider recovery requires an incremented lifecycle attempt.");
        if (Envelope is not null)
        {
            Envelope.Validate();
            if (Envelope.OrganizationId != Submission.OrganizationId ||
                Envelope.WorkspaceId != Submission.WorkspaceId ||
                Envelope.InstanceId != Submission.InstanceId ||
                Envelope.LifecycleOperationId != Submission.OperationId ||
                Envelope.AcceptedLifecycleAttemptNumber != Submission.AttemptNumber)
                throw new InvalidOperationException("Provider recovery envelope does not match the lifecycle submission.");
        }
    }
}

/// <summary>
/// Provider-neutral projection of the immutable recovery ledger. The Azure adapter
/// translates this value into its provider-owned observation binding; callers cannot
/// select another operation, workspace or plan through this envelope.
/// </summary>
public sealed record ElsaInstanceProviderRecoveryEnvelope(
    Guid RecoveryRequestId,
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid InstanceId,
    Guid LifecycleOperationId,
    int ObservedLifecycleAttemptNumber,
    int ObservedInstanceVersion,
    int AcceptedLifecycleAttemptNumber,
    int AcceptedInstanceVersion,
    string IdempotencyScope,
    string IdempotencyKey,
    string RequestHash,
    string ObservationReference,
    string ObservationDigest)
{
    public void Validate()
    {
        if (RecoveryRequestId == Guid.Empty || OrganizationId == Guid.Empty || WorkspaceId == Guid.Empty ||
            InstanceId == Guid.Empty || LifecycleOperationId == Guid.Empty ||
            ObservedLifecycleAttemptNumber < 1 || ObservedInstanceVersion < 1 ||
            AcceptedLifecycleAttemptNumber < 2 || AcceptedInstanceVersion < 1 ||
            string.IsNullOrWhiteSpace(IdempotencyScope) || IdempotencyScope.Length > 256 ||
            string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128 ||
            IdempotencyKey.Any(char.IsControl) || RequestHash is null || RequestHash.Length != 64 ||
            RequestHash.AsSpan().ContainsAnyExcept("0123456789abcdef") ||
            !ElsaInstanceProviderRecoveryObservationReference.TryParse(
                ObservationReference, out _, out var referenceDigest) ||
            !string.Equals(referenceDigest, ObservationDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("Provider recovery envelope is invalid.");
    }
}

public enum ElsaInstanceProviderRecoveryOutcome
{
    InProgress,
    RecoveryRequired,
    Succeeded,
    Failed,
    Rejected
}

public sealed record ElsaInstanceProviderRecoveryResult(
    ElsaInstanceProviderRecoveryOutcome Outcome,
    string Code,
    string Summary)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Outcome) || string.IsNullOrWhiteSpace(Code) || Code.Length > 128 ||
            Code.Any(char.IsControl) || string.IsNullOrWhiteSpace(Summary) || Summary.Length > 2000 ||
            Summary.Any(char.IsControl))
            throw new InvalidOperationException("Provider recovery result is invalid.");
    }
}

public enum ElsaInstanceProviderSubmissionFailureKind
{
    Rejected,
    OutcomeUnknown
}

/// <summary>
/// Classifies whether a failed provider submission was rejected before any durable provider
/// hand-off or may have been accepted without a response. Messages remain stable and value-free.
/// </summary>
public sealed class ElsaInstanceProviderSubmissionException(
    ElsaInstanceProviderSubmissionFailureKind kind,
    Exception? innerException = null)
    : Exception("Provider submission failed.", innerException)
{
    public ElsaInstanceProviderSubmissionFailureKind Kind { get; } = Enum.IsDefined(kind)
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(kind));
}

public sealed record ElsaInstanceProviderSubmission(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    int AttemptNumber,
    ElsaDesiredLifecycle DesiredLifecycle,
    ResolvedElsaApplicationPlan Plan,
    ElsaInstanceLifecycleDeploymentTarget DeploymentTarget,
    string? Location = null,
    Guid? OrganizationId = null,
    ElsaInstanceOperationAction? OperationAction = null,
    string? PlacementAssignmentId = null)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty ||
            OrganizationId is null || OrganizationId == Guid.Empty || OperationAction is null ||
            !Enum.IsDefined(OperationAction.Value) || AttemptNumber < 1 ||
            PlacementAssignmentId is not null && !Guid.TryParseExact(PlacementAssignmentId, "D", out _))
            throw new InvalidOperationException("Provider submission identity is invalid.");
        ArgumentNullException.ThrowIfNull(Plan);
        DeploymentTarget.Validate();
        if (DesiredLifecycle == ElsaDesiredLifecycle.Deleting)
            throw new InvalidOperationException("Deletion is not a resolved-provider apply.");
    }
}

public sealed record ElsaInstanceProviderSubmissionResult(
    string CorrelationId,
    bool Replayed,
    string? PlacementAssignmentId = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CorrelationId) || CorrelationId.Length > 128 ||
            CorrelationId.Any(char.IsControl) || CorrelationId.Any(char.IsWhiteSpace) ||
            PlacementAssignmentId is not null && !Guid.TryParseExact(PlacementAssignmentId, "D", out _))
            throw new InvalidOperationException("Provider submission correlation is invalid.");
    }
}
