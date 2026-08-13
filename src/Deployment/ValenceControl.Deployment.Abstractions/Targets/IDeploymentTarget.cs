namespace ValenceControl.Deployment.Abstractions.Targets;

/// <summary>
/// Represents a destination that deployment handlers can read from and apply to.
/// </summary>
public interface IDeploymentTarget
{
    DeploymentTargetDescriptor Descriptor { get; }
}
