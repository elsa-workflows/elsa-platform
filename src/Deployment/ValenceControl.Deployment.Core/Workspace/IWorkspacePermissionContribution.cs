namespace ValenceControl.Deployment.Core.Workspace;

/// <summary>
/// Additive permission vocabulary contributed by a bounded Control subsystem.
/// </summary>
public interface IWorkspacePermissionContribution
{
    IReadOnlySet<string> All { get; }
    IReadOnlySet<string> OwnerDefaults { get; }
}
