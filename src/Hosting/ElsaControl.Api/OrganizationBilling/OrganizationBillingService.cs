using ElsaControl.Billing.Stripe;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.OrganizationBilling;

public sealed class OrganizationBillingApiService(
    AccountWorkspaceService accounts,
    OrganizationBillingService billing,
    IBillingProvider provider,
    IOptions<StripeBillingOptions> stripeOptions)
{
    private readonly StripeBillingOptions _stripeOptions = stripeOptions.Value;

    public async Task<OrganizationBillingApiResult> CreateCheckoutAsync(
        TrustedWorkspaceIdentity identity,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var access = await accounts.GetOrganizationAccessAsync(identity, organizationId, OrganizationOperation.ManageBilling, cancellationToken);
        if (!access.Succeeded)
            return OrganizationBillingApiResult.Denied(access.Failure!.Value);

        if (!IsStripeProvider)
            return OrganizationBillingApiResult.Unavailable();

        if (!_stripeOptions.IsCheckoutConfigured)
            return OrganizationBillingApiResult.Unavailable();

        var trial = await billing.StartTrialAsync(organizationId, provider.Provider, cancellationToken);
        var subscription = trial.Subscription ?? await billing.GetSubscriptionAsync(organizationId, cancellationToken);
        if (subscription is null)
            return OrganizationBillingApiResult.Unavailable();

        try
        {
            var session = await provider.CreateCheckoutSessionAsync(
                new BillingCheckoutSessionRequest(
                    organizationId,
                    _stripeOptions.DefaultPriceId!,
                    _stripeOptions.CheckoutSuccessUrl!,
                    _stripeOptions.CheckoutCancelUrl!,
                    subscription.TrialEndsAt,
                    subscription.ProviderCustomerReference),
                cancellationToken);
            return OrganizationBillingApiResult.Success(session);
        }
        catch (BillingProviderUnavailableException)
        {
            return OrganizationBillingApiResult.Unavailable();
        }
    }

    public async Task<OrganizationBillingApiResult> CreatePortalAsync(
        TrustedWorkspaceIdentity identity,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var access = await accounts.GetOrganizationAccessAsync(identity, organizationId, OrganizationOperation.ManageBilling, cancellationToken);
        if (!access.Succeeded)
            return OrganizationBillingApiResult.Denied(access.Failure!.Value);

        if (!IsStripeProvider)
            return OrganizationBillingApiResult.Unavailable();

        if (!_stripeOptions.IsPortalConfigured)
            return OrganizationBillingApiResult.Unavailable();

        var subscription = await billing.GetSubscriptionAsync(organizationId, cancellationToken);
        if (subscription is null || string.IsNullOrWhiteSpace(subscription.ProviderCustomerReference))
            return OrganizationBillingApiResult.CustomerUnavailable();

        try
        {
            var session = await provider.CreateCustomerPortalSessionAsync(
                new BillingCustomerPortalSessionRequest(
                    organizationId,
                    subscription.ProviderCustomerReference,
                    _stripeOptions.PortalReturnUrl!),
                cancellationToken);
            return OrganizationBillingApiResult.Success(session);
        }
        catch (BillingProviderUnavailableException)
        {
            return OrganizationBillingApiResult.Unavailable();
        }
    }

    private bool IsStripeProvider => string.Equals(provider.Provider, BillingProviderNames.Stripe, StringComparison.Ordinal);
}

public sealed record OrganizationBillingApiResult(
    BillingSessionLink? Session,
    OrganizationWorkspaceFailure? Failure,
    bool ProviderUnavailable,
    bool CustomerNotReady)
{
    public bool Succeeded => Session is not null && Failure is null && !ProviderUnavailable && !CustomerNotReady;

    public static OrganizationBillingApiResult Success(BillingSessionLink session) => new(session, null, false, false);
    public static OrganizationBillingApiResult Denied(OrganizationWorkspaceFailure failure) => new(null, failure, false, false);
    public static OrganizationBillingApiResult Unavailable() => new(null, null, true, false);
    public static OrganizationBillingApiResult CustomerUnavailable() => new(null, null, false, true);
}
