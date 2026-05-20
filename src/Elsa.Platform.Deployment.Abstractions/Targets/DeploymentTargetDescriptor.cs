using Elsa.Platform.Deployment.Abstractions;
using static Elsa.Platform.Deployment.Abstractions.DeploymentGuard;

namespace Elsa.Platform.Deployment.Abstractions.Targets;

/// <summary>
/// Describes a deployment destination without carrying credentials.
/// </summary>
public sealed record DeploymentTargetDescriptor
{
    public DeploymentTargetDescriptor(
        string id,
        string? displayName = null,
        string? environment = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Id = Require(id, nameof(id));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        Environment = string.IsNullOrWhiteSpace(environment) ? null : environment.Trim();
        Properties = properties ?? EmptyProperties;
    }

    public string Id { get; }

    public string? DisplayName { get; }

    public string? Environment { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties = DeploymentEmpty.StringDictionary;
}
