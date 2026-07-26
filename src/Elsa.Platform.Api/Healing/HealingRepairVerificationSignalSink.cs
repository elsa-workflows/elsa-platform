using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Api.Healing;

public sealed class HealingRepairVerificationSignalSink(
    IRepairVerificationFailedSignalOutbox outbox) : IRepairVerificationSignalSink
{
    public async ValueTask AppendAsync(
        RepairVerificationFailedSignal failed,
        CancellationToken cancellationToken = default)
    {
        _ = await outbox.AppendAsync(failed, cancellationToken);
    }
}
