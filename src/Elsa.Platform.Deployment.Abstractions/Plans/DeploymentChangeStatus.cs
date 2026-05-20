namespace Elsa.Platform.Deployment.Abstractions.Plans;

/// <summary>
/// Describes the lifecycle status of a deployment change.
/// </summary>
public enum DeploymentChangeStatus
{
    Pending,
    Ready,
    Blocked,
    Skipped,
    Completed,
    Failed
}
