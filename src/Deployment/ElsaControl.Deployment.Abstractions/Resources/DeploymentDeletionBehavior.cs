namespace ElsaControl.Deployment.Abstractions.Resources;

/// <summary>
/// Describes how a resource may be deleted during reconciliation.
/// </summary>
public enum DeploymentDeletionBehavior
{
    Retain,
    Delete
}
