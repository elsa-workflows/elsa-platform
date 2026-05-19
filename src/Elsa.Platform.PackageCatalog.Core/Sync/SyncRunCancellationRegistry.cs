using System.Collections.Concurrent;

namespace Elsa.Platform.PackageCatalog.Core.Sync;

public sealed class SyncRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, RegisteredSyncRun> _runningRuns = new();

    public SyncRunCancellationScope Track(Guid runId, CancellationToken cancellationToken)
    {
        var run = new RegisteredSyncRun(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        if (!_runningRuns.TryAdd(runId, run))
            throw new InvalidOperationException($"Sync run '{runId}' is already tracked.");

        return new SyncRunCancellationScope(this, runId, run);
    }

    public bool Cancel(Guid runId)
    {
        if (!_runningRuns.TryGetValue(runId, out var run))
            return false;

        try
        {
            run.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    private void Untrack(Guid runId, RegisteredSyncRun run)
    {
        _runningRuns.TryRemove(new KeyValuePair<Guid, RegisteredSyncRun>(runId, run));
        run.Dispose();
    }

    public sealed class SyncRunCancellationScope : IDisposable
    {
        private readonly SyncRunCancellationRegistry _registry;
        private readonly Guid _runId;
        private readonly RegisteredSyncRun _run;

        internal SyncRunCancellationScope(SyncRunCancellationRegistry registry, Guid runId, RegisteredSyncRun run)
        {
            _registry = registry;
            _runId = runId;
            _run = run;
        }

        public CancellationToken Token => _run.Token;
        public bool IsOperatorCancellationRequested => _run.IsOperatorCancellationRequested;

        public void Dispose() => _registry.Untrack(_runId, _run);
    }

    internal sealed class RegisteredSyncRun(CancellationTokenSource cancellation) : IDisposable
    {
        private int _operatorCancellationRequested;

        public CancellationToken Token => cancellation.Token;
        public bool IsOperatorCancellationRequested => Volatile.Read(ref _operatorCancellationRequested) == 1;

        public void Cancel()
        {
            Interlocked.Exchange(ref _operatorCancellationRequested, 1);
            cancellation.Cancel();
        }

        public void Dispose() => cancellation.Dispose();
    }
}
