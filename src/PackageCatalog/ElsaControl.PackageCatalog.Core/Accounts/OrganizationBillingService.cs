namespace ElsaControl.PackageCatalog.Core.Accounts;

/// <summary>
/// Application boundary for consuming normalized billing facts. The EF
/// implementation supplies the transaction; this service keeps provider
/// adapters independent of persistence and authorization.
/// </summary>
public sealed class OrganizationBillingService(
    IOrganizationBillingStore store,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<BillingEventConsumptionResult> ConsumeAsync(
        BillingProviderEvent providerEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);
        return store.ConsumeAsync(providerEvent, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<BillingEventConsumptionResult> StartTrialAsync(
        Guid organizationId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return store.StartTrialAsync(
            organizationId,
            provider,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<OrganizationSubscription?> GetSubscriptionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        store.GetSubscriptionAsync(organizationId, cancellationToken);
}
