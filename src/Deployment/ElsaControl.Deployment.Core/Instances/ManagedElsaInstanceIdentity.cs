namespace ElsaControl.Deployment.Core.Instances;

public sealed record ManagedElsaInstanceIdentity(
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid InstanceId,
    string Audience,
    Uri CallbackUri,
    int BindingVersion);

public interface IManagedElsaInstanceIdentityStore
{
    Task<ManagedElsaInstanceIdentity?> FindAsync(
        Guid organizationId,
        Guid instanceId,
        CancellationToken cancellationToken = default);
}
