using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Abstractions.Plans;

/// <summary>
/// A planned resource-specific deployment action.
/// </summary>
public sealed record DeploymentChange
{
    public DeploymentChange(
        DeploymentResourceId resourceId,
        DeploymentChangeAction action,
        DeploymentChangeStatus status = DeploymentChangeStatus.Pending,
        string? reason = null,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DeploymentResource? resource = null)
    {
        ResourceId = resourceId;
        Action = action;
        Status = status;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Diagnostics = (diagnostics ?? []).ToArray();
        Resource = resource;
    }

    public DeploymentResourceId ResourceId { get; }

    public DeploymentChangeAction Action { get; }

    public DeploymentChangeStatus Status { get; }

    public string? Reason { get; }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics { get; }

    public DeploymentResource? Resource { get; }
}
