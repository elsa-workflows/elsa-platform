namespace ValenceControl.Deployment.Engine;

public sealed class DeploymentEngineOptions
{
    public Func<string> DeploymentIdFactory { get; init; } = () => $"deploy-{Guid.NewGuid():N}";

    public Func<string> PlanIdFactory { get; init; } = () => $"plan-{Guid.NewGuid():N}";

    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
}
