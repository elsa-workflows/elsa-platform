namespace Elsa.Platform.Deployment.Abstractions;

/// <summary>
/// Represents the overall outcome of a deployment operation.
/// </summary>
public enum DeploymentStatus
{
    NotStarted,
    Validated,
    DryRunCompleted,
    Applied,
    NoOp,
    CompletedWithWarnings,
    ValidationFailed,
    PartiallyApplied,
    Failed,
    Cancelled
}
