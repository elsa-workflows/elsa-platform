using System.Collections.Concurrent;

namespace ValenceControl.PackageCatalog.Core.Sync;

public sealed class SourceSyncActivityTracker
{
    private readonly ConcurrentDictionary<Guid, int> _activeSources = new();

    public IDisposable BeginSourceSync(Guid sourceId)
    {
        _activeSources.AddOrUpdate(sourceId, 1, (_, count) => count + 1);
        return new ActivityScope(this, sourceId);
    }

    public bool IsSourceSyncing(Guid sourceId) =>
        _activeSources.ContainsKey(sourceId);

    public IReadOnlySet<Guid> GetSyncingSourceIds() =>
        _activeSources.Keys.ToHashSet();

    private void EndSourceSync(Guid sourceId)
    {
        while (_activeSources.TryGetValue(sourceId, out var count))
        {
            if (count <= 1)
            {
                if (_activeSources.TryRemove(new KeyValuePair<Guid, int>(sourceId, count)))
                    return;
            }
            else if (_activeSources.TryUpdate(sourceId, count - 1, count))
            {
                return;
            }
        }
    }

    private sealed class ActivityScope(SourceSyncActivityTracker tracker, Guid sourceId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                tracker.EndSourceSync(sourceId);
        }
    }
}
