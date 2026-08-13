using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Repairs;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingMergeEvaluationStore(HealingDbContext dbContext) : IHealingMergeEvaluationStore
{
    public ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default) =>
        HealingPersistenceTransaction.ExecuteAsync(dbContext, operation, cancellationToken);

    public async ValueTask SaveAsync(
        PolicyEvaluation evaluation,
        Guid pullRequestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Merge evaluations must be saved inside the caller's transaction.");

        var pullRequest = await dbContext.RepairPullRequests.SingleOrDefaultAsync(
            x => x.Id == pullRequestId &&
                 x.WorkspaceId == evaluation.WorkspaceId &&
                 x.ApplicationId == evaluation.ApplicationId &&
                 x.AttemptId == evaluation.AttemptId,
            cancellationToken) ?? throw new InvalidOperationException(
            "The merge evaluation is not bound to the requested pull request.");
        var policyExists = await dbContext.MergePolicies.AsNoTracking().AnyAsync(
            x => x.Id == evaluation.PolicyId &&
                 x.WorkspaceId == evaluation.WorkspaceId &&
                 x.ApplicationId == evaluation.ApplicationId &&
                 x.PolicyVersion == evaluation.PolicyVersion &&
                 x.PolicyHash == evaluation.PolicyHash,
            cancellationToken);
        if (!policyExists || evaluation.PolicyKind != PolicyKind.Merge)
            throw new InvalidOperationException("The merge evaluation policy is not current for the requested tenant.");

        evaluation.Id = evaluation.Id == Guid.Empty ? Guid.NewGuid() : evaluation.Id;
        dbContext.PolicyEvaluations.Add(evaluation);
        pullRequest.MergePolicyEvaluationId = evaluation.Id;
        pullRequest.Version = Guid.NewGuid().ToByteArray();
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
