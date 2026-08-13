using static ValenceControl.Deployment.Abstractions.DeploymentGuard;

namespace ValenceControl.Deployment.Abstractions.History;

/// <summary>
/// Actor metadata associated with a deployment attempt.
/// </summary>
public sealed record DeploymentActor
{
    public DeploymentActor(string id, string? displayName = null)
    {
        Id = Require(id, nameof(id));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public string Id { get; }

    public string? DisplayName { get; }
}
