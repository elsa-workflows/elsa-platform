using ValenceControl.Healing.OpenTelemetry;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.Healing.Core;

namespace ValenceControl.Api.Healing;

public sealed class ControlHealingSignalInboxAppender(HealingStore store) : IHealingSignalInboxAppender
{
    public async ValueTask AppendAsync(
        HealingSignalInboxItem item,
        CancellationToken cancellationToken = default) =>
        _ = await store.AppendInboxAsync(item, cancellationToken);
}
