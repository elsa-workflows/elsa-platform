using System.Threading.Channels;
using Elsa.Catalog.Core.Sync;

namespace Elsa.Catalog.Api.Admin.Sync;

public sealed class ManualSyncQueue
{
    private readonly Channel<PackageSyncWorkItem> _queue = Channel.CreateUnbounded<PackageSyncWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public void Enqueue(PackageSyncWorkItem workItem)
    {
        if (_queue.Writer.TryWrite(workItem))
            return;

        workItem.Dispose();
        throw new InvalidOperationException("Manual sync queue is not accepting work.");
    }

    public IAsyncEnumerable<PackageSyncWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);
}
