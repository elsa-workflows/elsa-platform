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

public sealed record ElsaInstanceProviderSubmission(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    int AttemptNumber,
    ElsaDesiredLifecycle DesiredLifecycle,
    ResolvedElsaApplicationPlan Plan,
    ElsaInstanceLifecycleDeploymentTarget DeploymentTarget,
    string? Location = null)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty || AttemptNumber < 1)
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
