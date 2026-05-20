namespace Elsa.Platform.Deployment.Abstractions;

/// <summary>
/// Describes the deployment operation being executed.
/// </summary>
public enum DeploymentOperationMode
{
    Validate,
    Diff,
    DryRun,
    Apply
}
