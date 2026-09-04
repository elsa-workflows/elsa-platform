using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Evaluates the current provider-neutral organization projection. This is kept
/// in the catalog persistence layer so API and workers consume the same row and
/// the lifecycle store can evaluate it inside its serializable admission tx.
/// </summary>
public sealed class EfCoreElsaInstanceCommercialGate(CatalogDbContext db) : IElsaInstanceCommercialGate
{
    public async Task<ElsaInstanceCommercialGateDecision> EvaluateAsync(
        Guid organizationId,
        ElsaInstanceOperationAction action,
        int? activeInstanceCount = null,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
            return Deny(ElsaInstanceCommercialOperation.EntitlementRequired, "The organization entitlement is unavailable.");

        // Safe exits are deliberately independent of commercial state.
        if (action is ElsaInstanceOperationAction.Stop or ElsaInstanceOperationAction.Delete)
            return ElsaInstanceCommercialGateDecision.Allow();

        var entitlement = await db.OrganizationEntitlementSnapshots
            .AsTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        if (entitlement is null || !entitlement.ManagedHostingEnabled)
            return Deny(ElsaInstanceCommercialOperation.EntitlementRequired, "Managed hosting is not enabled for this organization.");

        if (entitlement.SubscriptionState is null)
            return Deny(ElsaInstanceCommercialOperation.SubscriptionStateRequired, "The organization subscription lifecycle is unavailable.");

        if (entitlement.SubscriptionState is OrganizationSubscriptionState.Constrained or
            OrganizationSubscriptionState.Suspended or
            OrganizationSubscriptionState.Retained or
            OrganizationSubscriptionState.Deleted)
            return Deny(ElsaInstanceCommercialOperation.LifecycleConstrained, "The organization subscription does not permit managed-instance changes.");

        if (action == ElsaInstanceOperationAction.Create &&
            activeInstanceCount is { } count && count >= entitlement.MaxInstances)
            return Deny(ElsaInstanceCommercialOperation.InstanceLimitReached, "The organization has reached its managed-instance limit.");

        return ElsaInstanceCommercialGateDecision.Allow();
    }

    private static ElsaInstanceCommercialGateDecision Deny(string code, string summary) =>
        new(false, code, summary);
}
