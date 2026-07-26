using ValenceControl.PackageCatalog.Core.Packages;

namespace ValenceControl.PackageCatalog.Core.Sync;

public interface ISyncDiagnostics
{
    void SyncRunStarted(Guid syncRunId);
    void SyncRunCompleted(Guid syncRunId, SyncRunStatus status);
    void SyncItemFailed(Guid syncRunId, string? packageId, string? version, string error);
    void SuspiciousManifestChange(Guid syncRunId, string packageId, string version, string observedHash);
}

public sealed class NoopSyncDiagnostics : ISyncDiagnostics
{
    public void SyncRunStarted(Guid syncRunId) { }
    public void SyncRunCompleted(Guid syncRunId, SyncRunStatus status) { }
    public void SyncItemFailed(Guid syncRunId, string? packageId, string? version, string error) { }
    public void SuspiciousManifestChange(Guid syncRunId, string packageId, string version, string observedHash) { }
}
