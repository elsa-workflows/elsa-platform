using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Operations;

namespace Elsa.Platform.Healing.Core.Providers;

public sealed record ProviderOperationEnqueueRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid ProviderConnectionId,
    ProviderOperationKind Kind,
    string IdempotencyKey,
    string PayloadJson,
    Guid? IncidentId = null,
    Guid? AttemptId = null);

public sealed record ProviderOperationAppendResult(ProviderOperation Operation, bool IsReplay);

public interface IProviderOperationStore : IHealingLeasedOperationStore<ProviderOperation>
{
    ValueTask<ProviderOperationAppendResult> AppendAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default);
}

public interface IProviderOperationHandler
{
    ProviderOperationKind Kind { get; }

    ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists provider mutations before dispatch and executes them through the shared bounded lease runner.
/// Provider handlers must use the operation idempotency key for every external mutation.
/// </summary>
public sealed class ProviderOperationService
{
    private readonly IProviderOperationStore _store;
    private readonly HealingOperationWorker<ProviderOperation> _worker;
    private readonly TimeProvider _timeProvider;
    private readonly bool _dispatchEnabled;

    public ProviderOperationService(
        IProviderOperationStore store,
        IEnumerable<IProviderOperationHandler> handlers,
        HealingOptions options,
        string workerId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatchEnabled = options.RepairDispatchEnabled;
        _worker = new HealingOperationWorker<ProviderOperation>(
            new AttemptLimitedStore(store, Math.Max(1, options.Budgets.MaxRepositoryRuns)),
            new ProviderOperationRouter(handlers),
            options,
            workerId,
            _timeProvider);
    }

    public async ValueTask<ProviderOperationAppendResult> EnqueueAsync(
        ProviderOperationEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId == Guid.Empty || request.ApplicationId == Guid.Empty || request.ProviderConnectionId == Guid.Empty)
            throw new ArgumentException("Workspace, application, and provider connection are required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(request));

        var canonicalPayload = CanonicalizeJson(request.PayloadJson);
        var now = _timeProvider.GetUtcNow();
        return await _store.AppendAsync(new ProviderOperation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            ApplicationId = request.ApplicationId,
            ProviderConnectionId = request.ProviderConnectionId,
            IncidentId = request.IncidentId,
            AttemptId = request.AttemptId,
            Kind = request.Kind,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            PayloadJson = canonicalPayload,
            PayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))),
            Status = ProviderOperationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
    }

    public ValueTask<HealingWorkerRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        _dispatchEnabled
            ? _worker.RunOnceAsync(cancellationToken)
            : ValueTask.FromResult(new HealingWorkerRunResult(HealingWorkerRunStatus.Paused, 0));

    public Task RunContinuouslyAsync(CancellationToken cancellationToken) =>
        _dispatchEnabled
            ? _worker.RunContinuouslyAsync(cancellationToken)
            : Task.CompletedTask;

    private static string CanonicalizeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("A bounded provider payload is required.", nameof(json));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        return JsonSerializer.Serialize(document.RootElement);
    }

    private sealed class ProviderOperationRouter : IHealingOperationHandler<ProviderOperation>
    {
        private readonly IReadOnlyDictionary<ProviderOperationKind, IProviderOperationHandler> _handlers;

        public ProviderOperationRouter(IEnumerable<IProviderOperationHandler> handlers)
        {
            var materialized = handlers.ToArray();
            var duplicate = materialized.GroupBy(x => x.Kind).FirstOrDefault(x => x.Count() > 1);
            if (duplicate is not null)
                throw new InvalidOperationException($"Multiple provider handlers are registered for '{duplicate.Key}'.");
            _handlers = materialized.ToDictionary(x => x.Kind);
        }

        public ValueTask<HealingOperationOutcome> ExecuteAsync(
            ProviderOperation operation,
            CancellationToken cancellationToken = default)
        {
            if (!_handlers.TryGetValue(operation.Kind, out var handler))
                return ValueTask.FromResult(HealingOperationOutcome.DeadLettered(
                    "provider-operation-handler-not-configured",
                    "No trusted provider handler is configured for this operation kind."));
            return handler.ExecuteAsync(operation, cancellationToken);
        }
    }

    private sealed class AttemptLimitedStore(IProviderOperationStore inner, int attemptLimit)
        : IHealingLeasedOperationStore<ProviderOperation>
    {
        public ValueTask<int> RecoverStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.RecoverStaleLeasesAsync(now, cancellationToken);

        public async ValueTask<HealingOperationLease<ProviderOperation>?> TryLeaseNextAsync(
            string workerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            var lease = await inner.TryLeaseNextAsync(workerId, now, leaseDuration, cancellationToken);
            return lease is null
                ? null
                : lease with { AttemptLimit = attemptLimit };
        }

        public ValueTask FinishAsync(
            HealingOperationLease<ProviderOperation> lease,
            HealingOperationOutcome outcome,
            DateTimeOffset finishedAt,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken = default) =>
            inner.FinishAsync(lease, outcome, finishedAt, nextAttemptAt, cancellationToken);
    }
}
