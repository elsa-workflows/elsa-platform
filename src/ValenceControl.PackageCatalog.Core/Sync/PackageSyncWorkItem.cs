using ValenceControl.PackageCatalog.Core.Packages;

namespace ValenceControl.PackageCatalog.Core.Sync;

public sealed class PackageSyncStartResult
{
    private PackageSyncStartResult(SyncRun run, PackageSyncWorkItem? workItem, SyncRunSourceReference? source)
    {
        Run = run;
        WorkItem = workItem;
        Source = source;
    }

    public SyncRun Run { get; }
    public PackageSyncWorkItem? WorkItem { get; }
    public SyncRunSourceReference? Source { get; }
    public bool Accepted => WorkItem is not null;

    public static PackageSyncStartResult AcceptedRun(SyncRun run, PackageSyncWorkItem workItem, SyncRunSourceReference? source = null) =>
        new(run, workItem, source);

    public static PackageSyncStartResult Rejected(SyncRun run) =>
        new(run, null, null);
}

public sealed class PackageSyncWorkItem(Guid runId, SyncRunTrigger trigger, Guid? sourceId, IDisposable concurrencyLease) : IDisposable
{
    private int _disposed;

    public Guid RunId { get; } = runId;
    public SyncRunTrigger Trigger { get; } = trigger;
    public Guid? SourceId { get; } = sourceId;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            concurrencyLease.Dispose();
    }
}
