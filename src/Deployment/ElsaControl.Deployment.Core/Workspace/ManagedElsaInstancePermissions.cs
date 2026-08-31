namespace ElsaControl.Deployment.Core.Workspace;

public static class ManagedElsaInstancePermissions
{
    public const string Open = "instances.open";
    public const string Delete = "instances.delete";
}

public sealed class ManagedElsaInstancePermissionContribution : IWorkspacePermissionContribution
{
    public IReadOnlySet<string> All { get; } = new HashSet<string>(
        [ManagedElsaInstancePermissions.Open, ManagedElsaInstancePermissions.Delete],
        StringComparer.Ordinal);

    public IReadOnlySet<string> OwnerDefaults => All;
}
