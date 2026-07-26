using ValenceControl.Deployment.Abstractions.History;

namespace ValenceControl.Deployment.Abstractions;

/// <summary>
/// Host-neutral per-run deployment context.
/// </summary>
public sealed record DeploymentExecutionContext
{
    public DeploymentExecutionContext(DeploymentActor? actor = null, bool prune = false)
    {
        Actor = actor;
        Prune = prune;
    }

    public DeploymentActor? Actor { get; }

    public bool Prune { get; }

    public static DeploymentExecutionContext Default { get; } = new();
}
