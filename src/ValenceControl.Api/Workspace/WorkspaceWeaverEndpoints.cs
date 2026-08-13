using ValenceControl.Api.Authentication;
using ValenceControl.Weaver.Core.Plans;
using ValenceControl.Weaver.Core.Sessions;

namespace ValenceControl.Api.Workspace;

public static class WorkspaceWeaverEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceWeaverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/weaver")
            .WithTags("Workspace Weaver")
            .MapCommonApiExceptions();

        group.MapGet("/configuration", (WeaverSessionService weaver) =>
        {
            var availability = weaver.GetAvailability();
            return Results.Ok(new WorkspaceWeaverConfigurationResponse(
                availability.Enabled,
                availability.ProviderMode,
                availability.Model,
                availability.ReasoningEffort,
                availability.StreamingEnabled,
                availability.Modes,
                availability.DisabledReason));
        }).RequireWorkspaceAccess();

        group.MapPost("/sessions", async (
            Guid workspaceId,
            WorkspaceWeaverCreateSessionRequest request,
            HttpContext context,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = context.GetWorkspaceAccess();
            try
            {
                var session = await weaver.CreateSessionAsync(
                    workspaceId,
                    access.OrganizationId == Guid.Empty ? null : access.OrganizationId,
                    access.AccountId,
                    new CreateWeaverSessionRequest(request.RoutePath, request.Mode, request.Context ?? new Dictionary<string, string>()),
                    cancellationToken);
                return Results.Created(
                    $"/api/workspaces/{workspaceId:D}/weaver/sessions/{session.Id:D}",
                    ToSessionResponse(session));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireWorkspaceAccess();

        group.MapGet("/sessions/{sessionId:guid}", async (
            Guid workspaceId,
            Guid sessionId,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
            Results.Ok(ToDetailResponse(await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken))))
            .RequireWorkspaceAccess();

        group.MapPost("/sessions/{sessionId:guid}/messages", async (
            Guid workspaceId,
            Guid sessionId,
            WorkspaceWeaverSendMessageRequest request,
            HttpContext context,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.Problem(title: "Prompt is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await weaver.SendMessageAsync(
                workspaceId,
                sessionId,
                context.GetWorkspaceAccess().AccountId,
                new SendWeaverMessageRequest(request.Prompt, request.Mode),
                cancellationToken);
            return Results.Ok(new WorkspaceWeaverSendMessageResponse(result.MessageId, result.AssistantMessageId, result.SessionStatus));
        }).RequireWorkspaceAccess();

        group.MapGet("/sessions/{sessionId:guid}/events", async (
            Guid workspaceId,
            Guid sessionId,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken);
            return Results.Ok(new[] { new WorkspaceWeaverEventResponse("session.idle", null) });
        }).RequireWorkspaceAccess();

        group.MapPost("/sessions/{sessionId:guid}/cancel", async (
            Guid workspaceId,
            Guid sessionId,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken);
            return Results.Accepted();
        }).RequireWorkspaceAccess();

        group.MapGet("/plans/{planId:guid}", async (
            Guid workspaceId,
            Guid planId,
            IWeaverSessionStore store,
            CancellationToken cancellationToken) =>
        {
            var plan = await store.GetPlanAsync(workspaceId, planId, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(ToPlanResponse(plan));
        }).RequireWorkspaceAccess();

        group.MapPost("/plans/{planId:guid}/approvals", async (
            Guid workspaceId,
            Guid planId,
            WorkspaceWeaverPlanApprovalRequest request,
            HttpContext context,
            WeaverPlanExecutionService execution,
            CancellationToken cancellationToken) =>
        {
            var plan = await execution.RecordApprovalAsync(
                workspaceId,
                planId,
                request.Version,
                context.GetWorkspaceAccess().AccountId,
                request.Decision,
                request.ConfirmationId,
                request.Reason,
                cancellationToken);
            return Results.Ok(new WorkspaceWeaverPlanApprovalResponse(plan.Id, plan.Version, plan.Status));
        }).RequireWorkspaceOwner();

        group.MapPost("/plans/{planId:guid}/execute", async (
            Guid workspaceId,
            Guid planId,
            WorkspaceWeaverPlanExecuteRequest request,
            WeaverPlanExecutionService execution,
            CancellationToken cancellationToken) =>
        {
            var result = await execution.ExecuteAsync(workspaceId, planId, request.Version, cancellationToken);
            return Results.Ok(new WorkspaceWeaverPlanExecuteResponse(result.Id, result.Status, result.LinkedResourceJson));
        }).RequireWorkspaceOwner();

        return endpoints;
    }

    private static WorkspaceWeaverSessionResponse ToSessionResponse(WeaverSession session) =>
        new(session.Id, session.Status, session.Mode, session.CreatedAt);

    private static WorkspaceWeaverSessionDetailResponse ToDetailResponse(WeaverSessionDetail detail) =>
        new(
            ToSessionResponse(detail.Session),
            detail.Messages.Select(ToMessageResponse).ToList(),
            detail.ToolCalls.Select(ToToolCallResponse).ToList(),
            detail.Plans.Select(ToPlanResponse).ToList());

    private static WorkspaceWeaverMessageResponse ToMessageResponse(WeaverMessage message) =>
        new(message.Id, message.Role, message.Content, message.RedactionState, message.Sequence, message.CreatedAt);

    private static WorkspaceWeaverToolCallResponse ToToolCallResponse(WeaverToolCall toolCall) =>
        new(
            toolCall.Id,
            toolCall.ToolName,
            toolCall.ResultSummaryJson,
            toolCall.AuthorizationResult,
            toolCall.Status,
            toolCall.DurationMilliseconds,
            toolCall.CreatedAt,
            toolCall.CompletedAt);

    private static WorkspaceWeaverPlanResponse ToPlanResponse(WeaverPlan plan) =>
        new(
            plan.Id,
            plan.Version,
            plan.PlanType,
            plan.Title,
            plan.Summary,
            plan.TargetJson,
            plan.ImpactJson,
            plan.ValidationJson,
            plan.RollbackJson,
            plan.Risk,
            plan.Status,
            plan.CreatedAt,
            plan.UpdatedAt);
}
