namespace Elsa.Platform.Deployment.Abstractions.Plans;

/// <summary>
/// Describes the action proposed for a deployment resource.
/// </summary>
public enum DeploymentChangeAction
{
    Create,
    Update,
    Activate,
    Deactivate,
    Delete,
    NoOp,
    Unsupported,
    Conflict
}
