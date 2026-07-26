using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Security;

namespace ValenceControl.Healing.Core.Repairs;

public sealed record HealingMergeEvaluationRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid AttemptId,
    Guid PullRequestId,
    MergePolicy Policy,
    AutoMergeEligibilityInput Input,
    Guid CorrelationId,
    Guid? CausationId = null);

public sealed record HealingMergeEvaluationResult(
    PolicyEvaluation Evaluation,
    PolicyEvaluationSnapshot Snapshot,
    bool AutomaticMergeAllowed);

/// <summary>
/// Persists an immutable merge evaluation and links it to the pull request in one transaction.
/// Implementations must reject cross-tenant identities and must not overwrite earlier evaluations.
/// </summary>
public interface IHealingMergeEvaluationStore
{
    ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        PolicyEvaluation evaluation,
        Guid pullRequestId,
        CancellationToken cancellationToken = default);
}

public sealed class HealingMergeService(
    IHealingMergeEvaluationStore store,
    HealingAuditService auditService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<HealingMergeEvaluationResult> EvaluateAsync(
        HealingMergeEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var now = _timeProvider.GetUtcNow();
        var snapshot = AutoMergeEligibilityPolicy.Evaluate(request.Policy, request.Input, now);
        var blockers = snapshot.Gates.Where(x => x.State != PolicyGateState.Pass).Select(x => x.ReasonCode).ToArray();
        var evaluation = new PolicyEvaluation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            ApplicationId = request.ApplicationId,
            AttemptId = request.AttemptId,
            PolicyId = request.Policy.Id,
            PolicyKind = PolicyKind.Merge,
            PolicyVersion = snapshot.PolicyVersion,
            PolicyHash = snapshot.PolicyHash,
            InputSnapshotHash = snapshot.InputDigest,
            GateResultsJson = JsonSerializer.Serialize(snapshot.Gates),
            Decision = ToDecision(snapshot.Decision),
            ReasonCodesJson = JsonSerializer.Serialize(blockers),
            EvaluatedAt = now
        };

        var automaticMergeAllowed = snapshot.Decision == PolicyDecisions.AllowAutomaticMerge;
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await store.SaveAsync(evaluation, request.PullRequestId, transactionCancellationToken);
            await auditService.AppendAsync(new HealingAuditWrite(
                request.WorkspaceId,
                "repair-pull-request",
                request.PullRequestId,
                "merge-eligibility-evaluated",
                automaticMergeAllowed ? "automatic-merge-allowed" : "automatic-merge-blocked",
                "control",
                "healing-merge-service",
                evaluation.Id,
                request.CorrelationId,
                snapshot.PolicyVersion,
                snapshot.InputDigest,
                HashOutput(evaluation.GateResultsJson, snapshot.Decision),
                new Dictionary<string, string?>
                {
                    ["status"] = snapshot.Decision,
                    ["gateReason"] = blockers.FirstOrDefault() ?? "all-gates-satisfied"
                }), transactionCancellationToken);
            return true;
        }, cancellationToken);

        return new HealingMergeEvaluationResult(evaluation, snapshot, automaticMergeAllowed);
    }

    private static PolicyDecision ToDecision(string decision) => decision switch
    {
        PolicyDecisions.AllowAutomaticMerge => PolicyDecision.AllowAutomaticMerge,
        PolicyDecisions.AllowPublication => PolicyDecision.AllowPublication,
        PolicyDecisions.HumanOnly => PolicyDecision.HumanOnly,
        _ => PolicyDecision.Deny
    };

    private static string HashOutput(string gateResultsJson, string decision) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{decision}\n{gateResultsJson}")));

    private static void ValidateRequest(HealingMergeEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Policy);
        ArgumentNullException.ThrowIfNull(request.Input);
        if (request.WorkspaceId == Guid.Empty || request.ApplicationId == Guid.Empty || request.AttemptId == Guid.Empty ||
            request.PullRequestId == Guid.Empty || request.CorrelationId == Guid.Empty)
            throw new ArgumentException("Tenant, attempt, pull-request, and correlation identities are required.", nameof(request));
        if (request.Policy.WorkspaceId != request.WorkspaceId || request.Policy.ApplicationId != request.ApplicationId)
            throw new ArgumentException("The merge policy must belong to the requested tenant scope.", nameof(request));
    }
}
