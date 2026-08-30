namespace ElsaControl.Deployment.Core.Workspace;

public static class ManagedElsaInstancePermissions
{
    public const string Open = "instances.open";
}

public sealed class ManagedElsaInstancePermissionContribution : IWorkspacePermissionContribution
{
    public IReadOnlySet<string> All { get; } = new HashSet<string>([ManagedElsaInstancePermissions.Open], StringComparer.Ordinal);

    public IReadOnlySet<string> OwnerDefaults => All;
}
