namespace ElsaControl.Deployment.Core.Instances;

public sealed record ManagedElsaInstanceIdentity(
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid InstanceId,
    string Audience,
    Uri CallbackUri,
    int BindingVersion);

public enum ManagedElsaInstanceIdentityBindingWriteOutcome
{
    Created,
    Rotated,
    NotFound,
    Conflict
}

public sealed record ManagedElsaInstanceIdentityBindingWriteResult(
    ManagedElsaInstanceIdentityBindingWriteOutcome Outcome,
    ManagedElsaInstanceIdentity? Identity)
{
    public bool Succeeded => Identity is not null &&
                             Outcome is ManagedElsaInstanceIdentityBindingWriteOutcome.Created or
                                 ManagedElsaInstanceIdentityBindingWriteOutcome.Rotated;
}

public interface IManagedElsaInstanceIdentityStore
{
    Task<ManagedElsaInstanceIdentity?> FindAsync(
        Guid organizationId,
        Guid instanceId,
        CancellationToken cancellationToken = default);

    Task<ManagedElsaInstanceIdentityBindingWriteResult> BindAsync(
        Guid organizationId,
        Guid workspaceId,
        Guid instanceId,
        string verifiedEndpointOrigin,
        int? expectedBindingVersion,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
