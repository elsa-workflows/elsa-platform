namespace Elsa.Platform.Deployment.Core.Workspace;

/// <summary>
/// Additive permission vocabulary contributed by a bounded Platform subsystem.
/// </summary>
public interface IWorkspacePermissionContribution
{
    IReadOnlySet<string> All { get; }
    IReadOnlySet<string> OwnerDefaults { get; }
}
