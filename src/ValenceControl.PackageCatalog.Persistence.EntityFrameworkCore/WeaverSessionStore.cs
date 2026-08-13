using System.Text.Json;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using ValenceControl.Weaver.Core.Sessions;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class WeaverSessionStore(CatalogDbContext dbContext) : IWeaverSessionStore
{
    public async Task<WeaverSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(session);
        await dbContext.WeaverSessions.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<WeaverSession?> GetSessionAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.WeaverSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == sessionId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<WeaverMessage>> ListMessagesAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(workspaceId, sessionId, cancellationToken))
            return [];

        var entities = await dbContext.WeaverMessages
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<WeaverMessage> AddMessageAsync(Guid workspaceId, WeaverMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureSessionExistsAsync(workspaceId, message.SessionId, cancellationToken);
        var entity = ToEntity(message);
        await dbContext.WeaverMessages.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<WeaverToolCall> AddToolCallAsync(Guid workspaceId, WeaverToolCall toolCall, CancellationToken cancellationToken = default)
    {
        await EnsureSessionExistsAsync(workspaceId, toolCall.SessionId, cancellationToken);
        var entity = ToEntity(toolCall);
        await dbContext.WeaverToolCalls.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<WeaverToolCall>> ListToolCallsAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(workspaceId, sessionId, cancellationToken))
            return [];

        var entities = await dbContext.WeaverToolCalls
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<WeaverPlan> AddPlanAsync(Guid workspaceId, WeaverPlan plan, CancellationToken cancellationToken = default)
    {
        await EnsureSessionExistsAsync(workspaceId, plan.SessionId, cancellationToken);
        var entity = ToEntity(plan);
        await dbContext.WeaverPlans.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<WeaverPlan?> GetPlanAsync(Guid workspaceId, Guid planId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.WeaverPlans
            .AsNoTracking()
            .Where(x => x.Id == planId && x.Session.WorkspaceId == workspaceId)
            .SingleOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<WeaverPlan>> ListPlansAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(workspaceId, sessionId, cancellationToken))
            return [];

        var entities = await dbContext.WeaverPlans
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Version)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<WeaverPlan> UpdatePlanStatusAsync(Guid workspaceId, Guid planId, int version, WeaverPlanStatus status, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.WeaverPlans
            .Include(x => x.Session)
            .SingleOrDefaultAsync(x => x.Id == planId && x.Version == version && x.Session.WorkspaceId == workspaceId, cancellationToken);
        if (entity is null)
            throw new KeyNotFoundException("Weaver plan does not exist in the workspace.");

        entity.Status = status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<WeaverPlanApproval> AddPlanApprovalAsync(Guid workspaceId, WeaverPlanApproval approval, CancellationToken cancellationToken = default)
    {
        await EnsurePlanExistsAsync(workspaceId, approval.PlanId, approval.PlanVersion, cancellationToken);
        var entity = ToEntity(approval);
        await dbContext.WeaverPlanApprovals.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<WeaverPlanExecution?> GetPlanExecutionAsync(Guid workspaceId, Guid planId, int planVersion, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.WeaverPlanExecutions
            .AsNoTracking()
            .Where(x => x.PlanId == planId && x.PlanVersion == planVersion && x.Plan.Session.WorkspaceId == workspaceId)
            .SingleOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<WeaverPlanExecution> AddPlanExecutionAsync(Guid workspaceId, WeaverPlanExecution execution, CancellationToken cancellationToken = default)
    {
        await EnsurePlanExistsAsync(workspaceId, execution.PlanId, execution.PlanVersion, cancellationToken);
        var entity = ToEntity(execution);
        await dbContext.WeaverPlanExecutions.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    private async Task EnsureSessionExistsAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (!await SessionExistsAsync(workspaceId, sessionId, cancellationToken))
            throw new KeyNotFoundException("Weaver session does not exist in the workspace.");
    }

    private Task<bool> SessionExistsAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.WeaverSessions.AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == sessionId, cancellationToken);

    private async Task EnsurePlanExistsAsync(Guid workspaceId, Guid planId, int version, CancellationToken cancellationToken)
    {
        if (!await dbContext.WeaverPlans.AnyAsync(x => x.Id == planId && x.Version == version && x.Session.WorkspaceId == workspaceId, cancellationToken))
            throw new KeyNotFoundException("Weaver plan does not exist in the workspace.");
    }

    private static WeaverSessionEntity ToEntity(WeaverSession session) => new()
    {
        Id = session.Id,
        WorkspaceId = session.WorkspaceId,
        OrganizationId = session.OrganizationId,
        AccountId = session.AccountId,
        CopilotSessionId = session.CopilotSessionId,
        RoutePath = session.RoutePath,
        ContextJson = session.Context?.RootElement.GetRawText(),
        Mode = session.Mode,
        ProviderMode = session.ProviderMode,
        Model = session.Model,
        ReasoningEffort = session.ReasoningEffort,
        Status = session.Status,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt,
        CompletedAt = session.CompletedAt
    };

    private static WeaverMessageEntity ToEntity(WeaverMessage message) => new()
    {
        Id = message.Id,
        SessionId = message.SessionId,
        Role = message.Role,
        Content = message.Content,
        RedactionState = message.RedactionState,
        Sequence = message.Sequence,
        CreatedAt = message.CreatedAt
    };

    private static WeaverToolCallEntity ToEntity(WeaverToolCall toolCall) => new()
    {
        Id = toolCall.Id,
        SessionId = toolCall.SessionId,
        ToolName = toolCall.ToolName,
        ArgumentsJson = toolCall.ArgumentsJson,
        ArgumentsHash = toolCall.ArgumentsHash,
        ResultSummaryJson = toolCall.ResultSummaryJson,
        AuthorizationResult = toolCall.AuthorizationResult,
        Status = toolCall.Status,
        DurationMilliseconds = toolCall.DurationMilliseconds,
        TraceId = toolCall.TraceId,
        CreatedAt = toolCall.CreatedAt,
        CompletedAt = toolCall.CompletedAt
    };

    private static WeaverPlanEntity ToEntity(WeaverPlan plan) => new()
    {
        Id = plan.Id,
        SessionId = plan.SessionId,
        Version = plan.Version,
        PlanType = plan.PlanType,
        Title = plan.Title,
        Summary = plan.Summary,
        TargetJson = plan.TargetJson,
        ImpactJson = plan.ImpactJson,
        ValidationJson = plan.ValidationJson,
        RollbackJson = plan.RollbackJson,
        Risk = plan.Risk,
        Status = plan.Status,
        CreatedByAccountId = plan.CreatedByAccountId,
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt
    };

    private static WeaverPlanApprovalEntity ToEntity(WeaverPlanApproval approval) => new()
    {
        Id = approval.Id,
        PlanId = approval.PlanId,
        PlanVersion = approval.PlanVersion,
        AccountId = approval.AccountId,
        Decision = approval.Decision,
        PermissionSnapshotJson = approval.PermissionSnapshotJson,
        ConfirmationId = approval.ConfirmationId,
        Reason = approval.Reason,
        CreatedAt = approval.CreatedAt
    };

    private static WeaverPlanExecutionEntity ToEntity(WeaverPlanExecution execution) => new()
    {
        Id = execution.Id,
        PlanId = execution.PlanId,
        PlanVersion = execution.PlanVersion,
        Status = execution.Status,
        LinkedResourceJson = execution.LinkedResourceJson,
        DiagnosticsJson = execution.DiagnosticsJson,
        TraceId = execution.TraceId,
        StartedAt = execution.StartedAt,
        CompletedAt = execution.CompletedAt
    };

    private static WeaverSession ToDomain(WeaverSessionEntity entity) => new(
        entity.Id,
        entity.WorkspaceId,
        entity.OrganizationId,
        entity.AccountId,
        entity.CopilotSessionId,
        entity.RoutePath,
        string.IsNullOrWhiteSpace(entity.ContextJson) ? null : JsonDocument.Parse(entity.ContextJson),
        entity.Mode,
        entity.ProviderMode,
        entity.Model,
        entity.ReasoningEffort,
        entity.Status,
        entity.CreatedAt,
        entity.UpdatedAt,
        entity.CompletedAt);

    private static WeaverMessage ToDomain(WeaverMessageEntity entity) => new(
        entity.Id,
        entity.SessionId,
        entity.Role,
        entity.Content,
        entity.RedactionState,
        entity.Sequence,
        entity.CreatedAt);

    private static WeaverToolCall ToDomain(WeaverToolCallEntity entity) => new(
        entity.Id,
        entity.SessionId,
        entity.ToolName,
        entity.ArgumentsJson,
        entity.ArgumentsHash,
        entity.ResultSummaryJson,
        entity.AuthorizationResult,
        entity.Status,
        entity.DurationMilliseconds,
        entity.TraceId,
        entity.CreatedAt,
        entity.CompletedAt);

    private static WeaverPlan ToDomain(WeaverPlanEntity entity) => new(
        entity.Id,
        entity.SessionId,
        entity.Version,
        entity.PlanType,
        entity.Title,
        entity.Summary,
        entity.TargetJson,
        entity.ImpactJson,
        entity.ValidationJson,
        entity.RollbackJson,
        entity.Risk,
        entity.Status,
        entity.CreatedByAccountId,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static WeaverPlanApproval ToDomain(WeaverPlanApprovalEntity entity) => new(
        entity.Id,
        entity.PlanId,
        entity.PlanVersion,
        entity.AccountId,
        entity.Decision,
        entity.PermissionSnapshotJson,
        entity.ConfirmationId,
        entity.Reason,
        entity.CreatedAt);

    private static WeaverPlanExecution ToDomain(WeaverPlanExecutionEntity entity) => new(
        entity.Id,
        entity.PlanId,
        entity.PlanVersion,
        entity.Status,
        entity.LinkedResourceJson,
        entity.DiagnosticsJson,
        entity.TraceId,
        entity.StartedAt,
        entity.CompletedAt);
}
