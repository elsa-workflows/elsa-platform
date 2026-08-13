using System.Text.Json;
using ValenceControl.Weaver.Core.Sessions;

namespace ValenceControl.Weaver.Core.Plans;

public sealed class WeaverPlanExecutionService(IWeaverSessionStore store, TimeProvider timeProvider)
{
    public async Task<WeaverPlan> RecordApprovalAsync(
        Guid workspaceId,
        Guid planId,
        int version,
        Guid accountId,
        WeaverPlanApprovalDecision decision,
        Guid? confirmationId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var plan = await store.GetPlanAsync(workspaceId, planId, cancellationToken)
            ?? throw new KeyNotFoundException("Weaver plan does not exist in the workspace.");
        if (plan.Version != version)
            throw new InvalidOperationException("Plan version does not match.");

        await store.AddPlanApprovalAsync(
            workspaceId,
            new WeaverPlanApproval(
                Guid.NewGuid(),
                planId,
                version,
                accountId,
                decision,
                JsonSerializer.Serialize(new { workspaceId, accountId }),
                confirmationId,
                reason,
                timeProvider.GetUtcNow()),
            cancellationToken);

        return await store.UpdatePlanStatusAsync(
            workspaceId,
            planId,
            version,
            decision == WeaverPlanApprovalDecision.Approved ? WeaverPlanStatus.Approved : WeaverPlanStatus.Rejected,
            cancellationToken);
    }

    public async Task<WeaverPlanExecution> ExecuteAsync(
        Guid workspaceId,
        Guid planId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.GetPlanExecutionAsync(workspaceId, planId, version, cancellationToken);
        if (existing is not null)
            return existing;

        var plan = await store.GetPlanAsync(workspaceId, planId, cancellationToken)
            ?? throw new KeyNotFoundException("Weaver plan does not exist in the workspace.");
        if (plan.Version != version)
            throw new InvalidOperationException("Plan version does not match.");
        if (plan.Status is not WeaverPlanStatus.Approved)
            throw new InvalidOperationException("Plan must be approved before execution.");

        await store.UpdatePlanStatusAsync(workspaceId, planId, version, WeaverPlanStatus.Executing, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var execution = await store.AddPlanExecutionAsync(
            workspaceId,
            new WeaverPlanExecution(
                Guid.NewGuid(),
                planId,
                version,
                WeaverPlanExecutionStatus.Succeeded,
                JsonSerializer.Serialize(new[] { new { type = "weaver.fakeExecution", planId, version } }),
                JsonSerializer.Serialize(new { message = "Fake Weaver execution recorded. No workspace mutation was performed." }),
                null,
                now,
                now),
            cancellationToken);
        await store.UpdatePlanStatusAsync(workspaceId, planId, version, WeaverPlanStatus.Succeeded, cancellationToken);
        return execution;
    }
}
