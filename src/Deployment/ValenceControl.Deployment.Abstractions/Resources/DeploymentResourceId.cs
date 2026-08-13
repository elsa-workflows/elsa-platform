using static ValenceControl.Deployment.Abstractions.DeploymentGuard;

namespace ValenceControl.Deployment.Abstractions.Resources;

/// <summary>
/// Stable logical identity for a deployable control-plane resource.
/// </summary>
public readonly record struct DeploymentResourceId
{
    public DeploymentResourceId(string type, string logicalId, string? scope = null)
    {
        Type = Require(type, nameof(type));
        LogicalId = Require(logicalId, nameof(logicalId));
        Scope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();
    }

    public string Type { get; }

    public string LogicalId { get; }

    public string? Scope { get; }

    public override string ToString() => Scope is null ? $"{Type}/{LogicalId}" : $"{Scope}:{Type}/{LogicalId}";
}
