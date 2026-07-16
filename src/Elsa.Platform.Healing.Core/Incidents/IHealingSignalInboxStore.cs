namespace Elsa.Platform.Healing.Core.Incidents;

public sealed record HealingInboxLease(HealingSignalInboxItem Item, string LeaseToken);

public interface IHealingSignalInboxStore
{
    ValueTask<HealingInboxLease?> TryLeaseNextAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CompleteAsync(
        Guid itemId,
        string leaseToken,
        DateTimeOffset now,
        HealingInboxStatus terminalStatus,
        string outcomeCode,
        string? safeOutcomeDetail,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RetryAsync(
        Guid itemId,
        string leaseToken,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt,
        string outcomeCode,
        string? safeOutcomeDetail,
        CancellationToken cancellationToken = default);
}
