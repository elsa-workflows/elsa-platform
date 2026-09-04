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
    ElsaInstanceOperationAction? OperationAction = null)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty ||
            OrganizationId is null || OrganizationId == Guid.Empty || OperationAction is null ||
            !Enum.IsDefined(OperationAction.Value) || AttemptNumber < 1)
            throw new InvalidOperationException("Provider submission identity is invalid.");
        ArgumentNullException.ThrowIfNull(Plan);
        DeploymentTarget.Validate();
        if (DesiredLifecycle == ElsaDesiredLifecycle.Deleting)
            throw new InvalidOperationException("Deletion is not a resolved-provider apply.");
    }
}

public sealed record ElsaInstanceProviderSubmissionResult(
    string CorrelationId,
    bool Replayed)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CorrelationId) || CorrelationId.Length > 128 ||
            CorrelationId.Any(char.IsControl) || CorrelationId.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Provider submission correlation is invalid.");
    }
}
