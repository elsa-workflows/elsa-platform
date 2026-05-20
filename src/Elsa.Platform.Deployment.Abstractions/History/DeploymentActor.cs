namespace Elsa.Platform.Deployment.Abstractions.History;

/// <summary>
/// Actor metadata associated with a deployment attempt.
/// </summary>
public sealed record DeploymentActor
{
    public DeploymentActor(string id, string? displayName = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Value cannot be empty.", nameof(id)) : id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public string Id { get; }

    public string? DisplayName { get; }
}
