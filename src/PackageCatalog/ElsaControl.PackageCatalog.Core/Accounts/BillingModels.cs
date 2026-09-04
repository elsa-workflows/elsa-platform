namespace ElsaControl.PackageCatalog.Core.Accounts;

/// <summary>
/// Shared bounds for provider-neutral billing references. The limit keeps
/// provider and reference pairs within SQL Server's composite index key size.
/// </summary>
public static class OrganizationBillingLimits
{
    public const int ProviderReferenceMaxLength = 256;
}

/// <summary>
/// Provider-neutral lifecycle owned by Elsa Control at the organization
/// boundary. Provider-specific plans and price names deliberately do not
/// appear in this model.
/// </summary>
public sealed class OrganizationSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderCustomerReference { get; set; }
    public string? ProviderSubscriptionReference { get; set; }
    public OrganizationSubscriptionState State { get; set; } = OrganizationSubscriptionState.Trial;
    public DateTimeOffset TrialStartedAt { get; set; }
    public DateTimeOffset TrialEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? PastDueAt { get; set; }
    public DateTimeOffset? ConstrainedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public DateTimeOffset? RetainedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset LastProviderEventOccurredAt { get; set; }
    public string? LastProviderEventId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The commercial state vocabulary is intentionally independent of a payment
/// provider's product or price names. Trialing is retained as an alias for
/// integrations that use the conventional payment vocabulary.
/// </summary>
public enum OrganizationSubscriptionState
{
    Trial = 0,
    Trialing = Trial,
    Active = 1,
    PastDue = 2,
    Constrained = 3,
    Suspended = 4,
    Retained = 5,
    Deleted = 6
}

/// <summary>
/// Safe, already-normalized facts from a billing provider. Raw webhook data,
/// credentials and tokens never cross this boundary.
/// </summary>
public sealed record BillingProviderEvent(
    Guid OrganizationId,
    string Provider,
    string ProviderEventId,
    string EventType,
    OrganizationSubscriptionState State,
    DateTimeOffset OccurredAt,
    string EventHash,
    string? ProviderCustomerReference = null,
    string? ProviderSubscriptionReference = null);

public enum BillingProviderEventProcessingStatus
{
    Accepted,
    Applied,
    IgnoredOutOfOrder,
    Rejected
}

/// <summary>
/// Durable inbox entry. It contains only normalized, bounded metadata.
/// </summary>
public sealed class BillingProviderEventInboxEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderEventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public OrganizationSubscriptionState State { get; set; }
    public string EventHash { get; set; } = "";
    public string? ProviderCustomerReference { get; set; }
    public string? ProviderSubscriptionReference { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public BillingProviderEventProcessingStatus ProcessingStatus { get; set; }
    public string? RejectionCode { get; set; }
}

public enum BillingEventConsumptionOutcome
{
    Applied,
    Replayed,
    IgnoredOutOfOrder,
    Rejected
}

public sealed record BillingEventConsumptionResult(
    BillingEventConsumptionOutcome Outcome,
    OrganizationSubscription? Subscription,
    OrganizationEntitlementSnapshot? Entitlement,
    BillingProviderEventInboxEntry? Event,
    string? RejectionCode = null);

public sealed class BillingProviderEventConflictException(string message) : InvalidOperationException(message);

public interface IOrganizationBillingStore
{
    Task<BillingEventConsumptionResult> ConsumeAsync(
        BillingProviderEvent providerEvent,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default);

    Task<BillingEventConsumptionResult> StartTrialAsync(
        Guid organizationId,
        string provider,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task<OrganizationSubscription?> GetSubscriptionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public static class OrganizationSubscriptionLifecycle
{
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(14);

    public static bool CanTransition(OrganizationSubscriptionState current, OrganizationSubscriptionState next) =>
        current == next || (current, next) switch
        {
            (OrganizationSubscriptionState.Trial, OrganizationSubscriptionState.Active or OrganizationSubscriptionState.PastDue) => true,
            (OrganizationSubscriptionState.Active, OrganizationSubscriptionState.PastDue or OrganizationSubscriptionState.Constrained or OrganizationSubscriptionState.Suspended) => true,
            (OrganizationSubscriptionState.PastDue, OrganizationSubscriptionState.Active or OrganizationSubscriptionState.Constrained or OrganizationSubscriptionState.Suspended) => true,
            (OrganizationSubscriptionState.Constrained, OrganizationSubscriptionState.Active or OrganizationSubscriptionState.Suspended) => true,
            (OrganizationSubscriptionState.Suspended, OrganizationSubscriptionState.Retained) => true,
            (OrganizationSubscriptionState.Retained, OrganizationSubscriptionState.Deleted) => true,
            _ => false
        };

    public static OrganizationSubscription CreateTrial(Guid organizationId, string provider, DateTimeOffset startedAt)
    {
        var started = startedAt.ToUniversalTime();
        return new OrganizationSubscription
        {
            OrganizationId = organizationId,
            Provider = provider,
            State = OrganizationSubscriptionState.Trial,
            TrialStartedAt = started,
            TrialEndsAt = started.Add(TrialDuration),
            LastProviderEventOccurredAt = started,
            CreatedAt = started,
            UpdatedAt = started
        };
    }

    public static void ApplyState(OrganizationSubscription subscription, OrganizationSubscriptionState next, DateTimeOffset occurredAt)
    {
        if (!CanTransition(subscription.State, next))
            throw new InvalidOperationException($"Subscription cannot transition from {subscription.State} to {next}.");

        var timestamp = occurredAt.ToUniversalTime();
        subscription.State = next;
        switch (next)
        {
            case OrganizationSubscriptionState.Active:
                subscription.ActivatedAt ??= timestamp;
                break;
            case OrganizationSubscriptionState.PastDue:
                subscription.PastDueAt ??= timestamp;
                break;
            case OrganizationSubscriptionState.Constrained:
                subscription.ConstrainedAt ??= timestamp;
                break;
            case OrganizationSubscriptionState.Suspended:
                subscription.SuspendedAt ??= timestamp;
                break;
            case OrganizationSubscriptionState.Retained:
                subscription.RetainedAt ??= timestamp;
                break;
            case OrganizationSubscriptionState.Deleted:
                subscription.DeletedAt ??= timestamp;
                break;
        }
    }
}
