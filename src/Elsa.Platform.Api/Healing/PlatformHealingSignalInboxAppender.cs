using Elsa.Platform.Healing.OpenTelemetry;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.Healing.Core;

namespace Elsa.Platform.Api.Healing;

public sealed class PlatformHealingSignalInboxAppender(HealingStore store) : IHealingSignalInboxAppender
{
    public async ValueTask AppendAsync(
        HealingSignalInboxItem item,
        CancellationToken cancellationToken = default) =>
        _ = await store.AppendInboxAsync(item, cancellationToken);
}
