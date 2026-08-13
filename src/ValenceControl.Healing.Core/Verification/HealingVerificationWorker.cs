using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Configuration;

namespace ValenceControl.Healing.Core.Verification;

public sealed class HealingVerificationWorker(
    IHealingVerificationStore store,
    HealingVerificationService service,
    TimeProvider timeProvider,
    HealingKillSwitch killSwitch)
{
    public async ValueTask<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!killSwitch.CanVerify().Allowed)
            return false;
        var now = timeProvider.GetUtcNow();
        var expiredWaivers = await store.ListExpiredWaiverScopesAsync(now, 100, cancellationToken);
        var changed = false;
        foreach (var scope in expiredWaivers)
            changed |= await service.ExpireWaiverAsync(scope, now, cancellationToken);
        var scopes = await store.ListDueScopesAsync(now, 100, cancellationToken);
        foreach (var scope in scopes)
            changed |= await service.EvaluateDueAsync(scope, now, cancellationToken);
        return changed;
    }

    public async ValueTask<bool> ReportRecurrenceAsync(
        IncidentOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        if (!killSwitch.CanVerify().Allowed)
            return false;
        var scope = await service.RecordRecurrenceAsync(occurrence, cancellationToken);
        return scope?.Verification?.SupportingOccurrenceId is not null;
    }
}
