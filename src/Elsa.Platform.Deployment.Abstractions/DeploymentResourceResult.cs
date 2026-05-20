using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Abstractions;

/// <summary>
/// Outcome for one resource operation within a deployment attempt.
/// </summary>
public sealed record DeploymentResourceResult
{
    public DeploymentResourceResult(
        DeploymentResourceId resourceId,
        DeploymentChangeAction action,
        DeploymentChangeStatus status,
        bool retryable = false,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        ResourceId = resourceId;
        Action = action;
        Status = status;
        Retryable = retryable;
        Diagnostics = (diagnostics ?? []).ToArray();
        StartedAt = (startedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        CompletedAt = completedAt?.ToUniversalTime();
    }

    public DeploymentResourceId ResourceId { get; }

    public DeploymentChangeAction Action { get; }

    public DeploymentChangeStatus Status { get; }

    public bool Retryable { get; }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }
}
