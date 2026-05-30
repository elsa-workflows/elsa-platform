using System.Collections.Concurrent;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class InMemoryWorkflowArtifactApplyJournal : IWorkflowArtifactApplyJournal
{
    private readonly ConcurrentDictionary<string, WorkflowArtifactApplyJournalEntry> _entries = new(StringComparer.Ordinal);

    public Task<WorkflowArtifactApplyJournalEntry?> FindAsync(
        string idempotencyKey,
        ArtifactDigest observedDigest,
        CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(Key(idempotencyKey, observedDigest), out var entry);
        return Task.FromResult(entry);
    }

    public Task RecordAsync(
        WorkflowArtifactApplyJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        _entries[Key(entry.IdempotencyKey, entry.ObservedDigest)] = entry;
        return Task.CompletedTask;
    }

    private static string Key(string idempotencyKey, ArtifactDigest digest) =>
        $"{idempotencyKey.Trim()}\n{digest.Algorithm.Trim().ToLowerInvariant()}:{digest.Value.Trim().ToLowerInvariant()}";
}

public sealed class JournaledWorkflowDefinitionApplier(
    IWorkflowDefinitionApplier inner,
    IWorkflowArtifactApplyJournal journal,
    TimeProvider? timeProvider = null) : IWorkflowDefinitionApplier
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<WorkflowArtifactApplyResult> ApplyAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await journal.FindAsync(request.IdempotencyKey, request.ObservedDigest, cancellationToken);
        if (existing is { Status: WorkflowArtifactApplyStatus.Applied or WorkflowArtifactApplyStatus.AlreadyApplied })
        {
            return new WorkflowArtifactApplyResult(
                WorkflowArtifactApplyStatus.AlreadyApplied,
                existing.ObservedDigest,
                existing.RuntimeReference,
                existing.Diagnostics);
        }

        var result = await inner.ApplyAsync(request, cancellationToken);
        if (result.Succeeded)
        {
            await journal.RecordAsync(
                new WorkflowArtifactApplyJournalEntry(
                    request.CommandId,
                    request.IdempotencyKey,
                    request.ObservedDigest,
                    result.Status,
                    result.RuntimeReference,
                    result.Diagnostics,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
        }

        return result;
    }
}
