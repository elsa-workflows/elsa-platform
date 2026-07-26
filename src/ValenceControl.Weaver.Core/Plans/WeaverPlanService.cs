using System.Text.Json;
using ValenceControl.Weaver.Core.Safety;
using ValenceControl.Weaver.Core.Sessions;

namespace ValenceControl.Weaver.Core.Plans;

public sealed class WeaverPlanService(
    IWeaverSessionStore store,
    WeaverRedactionService redaction,
    TimeProvider timeProvider)
{
    public async Task<WeaverPlan> DraftPlanAsync(
        Guid workspaceId,
        Guid sessionId,
        Guid accountId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var safePrompt = redaction.Redact(prompt).Value;
        var plans = await store.ListPlansAsync(workspaceId, sessionId, cancellationToken);
        var plan = new WeaverPlan(
            Guid.NewGuid(),
            sessionId,
            plans.Count == 0 ? 1 : plans.Max(x => x.Version) + 1,
            DetectPlanType(safePrompt),
            DraftTitle(safePrompt),
            safePrompt,
            JsonSerializer.Serialize(new { workspaceId, requestedBy = accountId }),
            JsonSerializer.Serialize(new { changes = "No workspace mutation will run until this plan is approved." }),
            JsonSerializer.Serialize(new { status = "Requires review", blockers = Array.Empty<string>() }),
            JsonSerializer.Serialize(new { path = "Use the previous deployed revision or cancel before approval." }),
            WeaverPlanRisk.Medium,
            WeaverPlanStatus.ReadyForApproval,
            accountId,
            now,
            now);

        return await store.AddPlanAsync(workspaceId, plan, cancellationToken);
    }

    public bool ShouldDraftPlan(WeaverMode mode, string prompt) =>
        mode is WeaverMode.Plan or WeaverMode.Operate &&
        (Contains(prompt, "plan") || Contains(prompt, "promote") || Contains(prompt, "deploy") || Contains(prompt, "rollback"));

    private static WeaverPlanType DetectPlanType(string prompt)
    {
        if (Contains(prompt, "promote") || Contains(prompt, "promotion"))
            return WeaverPlanType.Promotion;
        if (Contains(prompt, "rollback"))
            return WeaverPlanType.Rollback;
        if (Contains(prompt, "engine"))
            return WeaverPlanType.EngineRegistration;
        return WeaverPlanType.Deployment;
    }

    private static string DraftTitle(string prompt)
    {
        if (Contains(prompt, "promote") || Contains(prompt, "promotion"))
            return "Draft promotion plan";
        if (Contains(prompt, "rollback"))
            return "Draft rollback plan";
        if (Contains(prompt, "engine"))
            return "Draft engine plan";
        return "Draft deployment plan";
    }

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
