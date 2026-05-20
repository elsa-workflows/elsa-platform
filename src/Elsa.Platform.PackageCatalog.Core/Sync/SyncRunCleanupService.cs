using Elsa.Platform.PackageCatalog.Core.Packages;
using Microsoft.Extensions.Logging;

namespace Elsa.Platform.PackageCatalog.Core.Sync;

public sealed class SyncRunCleanupService(
    ISyncRunStore syncRuns,
    ILogger<SyncRunCleanupService> logger)
{
    private static readonly SyncRunStatus[] TerminalStatuses =
    [
        SyncRunStatus.Completed,
        SyncRunStatus.CompletedWithErrors,
        SyncRunStatus.Failed
    ];

    public async Task<SyncRunCleanupPreviewResult> PreviewDeleteBeforeAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken = default)
    {
        var cutoff = completedBefore.ToUniversalTime();
        if (cutoff > DateTimeOffset.UtcNow)
            return SyncRunCleanupPreviewResult.InvalidFutureCutoff(cutoff);

        var preview = await syncRuns.PreviewDeleteBeforeAsync(cutoff, TerminalStatuses, cancellationToken);
        return SyncRunCleanupPreviewResult.Valid(preview);
    }

    public async Task<SyncRunSingleDeleteResult> DeleteAsync(Guid id, string? actor = null, CancellationToken cancellationToken = default)
    {
        var candidate = await syncRuns.GetDeletionCandidateAsync(id, cancellationToken);
        if (candidate is null)
        {
            var notFound = new SyncRunCleanupResult(0, 0, 0, 1, null, []);
            LogSingleDelete(actor, id, notFound);
            return SyncRunSingleDeleteResult.Deleted(notFound);
        }

        if (!IsTerminal(candidate.Status))
            return SyncRunSingleDeleteResult.NonTerminal(candidate.Status);

        var result = await syncRuns.DeleteAsync(id, cancellationToken);
        LogSingleDelete(actor, id, result);
        return SyncRunSingleDeleteResult.Deleted(result);
    }

    public async Task<SyncRunBulkDeleteResult> DeleteBeforeAsync(DateTimeOffset completedBefore, string? actor = null, CancellationToken cancellationToken = default)
    {
        var cutoff = completedBefore.ToUniversalTime();
        if (cutoff > DateTimeOffset.UtcNow)
            return SyncRunBulkDeleteResult.InvalidFutureCutoff(cutoff);

        var result = await syncRuns.DeleteBeforeAsync(cutoff, TerminalStatuses, cancellationToken);
        logger.LogInformation(
            "Sync run bulk cleanup by {Actor}: completedBefore={CompletedBefore}, deletedRuns={DeletedRunCount}, deletedItems={DeletedItemCount}, excludedRuns={ExcludedRunCount}",
            actor ?? "unknown",
            cutoff,
            result.DeletedRunCount,
            result.DeletedItemCount,
            result.ExcludedRunCount);
        return SyncRunBulkDeleteResult.Deleted(result);
    }

    public static bool IsTerminal(SyncRunStatus status) => TerminalStatuses.Contains(status);

    private void LogSingleDelete(string? actor, Guid id, SyncRunCleanupResult result) =>
        logger.LogInformation(
            "Sync run cleanup by {Actor}: id={SyncRunId}, deletedRuns={DeletedRunCount}, deletedItems={DeletedItemCount}, notFoundRuns={NotFoundRunCount}",
            actor ?? "unknown",
            id,
            result.DeletedRunCount,
            result.DeletedItemCount,
            result.NotFoundRunCount);
}

public sealed record SyncRunCleanupPreviewResult(
    SyncRunCleanupPreview? Preview,
    DateTimeOffset? InvalidCutoff)
{
    public bool IsValid => Preview is not null;

    public static SyncRunCleanupPreviewResult Valid(SyncRunCleanupPreview preview) => new(preview, null);
    public static SyncRunCleanupPreviewResult InvalidFutureCutoff(DateTimeOffset cutoff) => new(null, cutoff);
}

public sealed record SyncRunSingleDeleteResult(
    SyncRunCleanupResult? Cleanup,
    SyncRunStatus? NonTerminalStatus)
{
    public bool IsConflict => NonTerminalStatus.HasValue;

    public static SyncRunSingleDeleteResult Deleted(SyncRunCleanupResult cleanup) => new(cleanup, null);
    public static SyncRunSingleDeleteResult NonTerminal(SyncRunStatus status) => new(null, status);
}

public sealed record SyncRunBulkDeleteResult(
    SyncRunCleanupResult? Cleanup,
    DateTimeOffset? InvalidCutoff)
{
    public bool IsValid => Cleanup is not null;

    public static SyncRunBulkDeleteResult Deleted(SyncRunCleanupResult cleanup) => new(cleanup, null);
    public static SyncRunBulkDeleteResult InvalidFutureCutoff(DateTimeOffset cutoff) => new(null, cutoff);
}
