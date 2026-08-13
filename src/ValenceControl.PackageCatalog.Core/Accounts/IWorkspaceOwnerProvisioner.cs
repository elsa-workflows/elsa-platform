namespace ValenceControl.PackageCatalog.Core.Accounts;

/// <summary>
/// Contributes domain-specific setup when an account becomes a workspace owner.
/// Implementations must be idempotent because ownership setup can be retried.
/// </summary>
public interface IWorkspaceOwnerProvisioner
{
    Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default);
}
