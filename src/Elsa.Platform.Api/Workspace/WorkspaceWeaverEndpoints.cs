using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.Weaver.Core.Plans;
using Elsa.Platform.Weaver.Core.Sessions;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceWeaverEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceWeaverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/weaver")
            .WithTags("Workspace Weaver");

        group.MapGet("/configuration", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var availability = weaver.GetAvailability();
            return Results.Ok(new WorkspaceWeaverConfigurationResponse(
                availability.Enabled,
                availability.ProviderMode,
                availability.Model,
                availability.ReasoningEffort,
                availability.StreamingEnabled,
                availability.Modes,
                availability.DisabledReason));
        });

        group.MapPost("/sessions", async (
            Guid workspaceId,
            WorkspaceWeaverCreateSessionRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            try
            {
                var session = await weaver.CreateSessionAsync(
                    workspaceId,
                    access.Access!.OrganizationId == Guid.Empty ? null : access.Access.OrganizationId,
                    access.Access.AccountId,
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
        });

        group.MapGet("/sessions/{sessionId:guid}", async (
            Guid workspaceId,
            Guid sessionId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            try
            {
                return Results.Ok(ToDetailResponse(await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken)));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/sessions/{sessionId:guid}/messages", async (
            Guid workspaceId,
            Guid sessionId,
            WorkspaceWeaverSendMessageRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.Problem(title: "Prompt is required.", statusCode: StatusCodes.Status400BadRequest);

            try
            {
                var result = await weaver.SendMessageAsync(
                    workspaceId,
                    sessionId,
                    access.Access!.AccountId,
                    new SendWeaverMessageRequest(request.Prompt, request.Mode),
                    cancellationToken);
                return Results.Ok(new WorkspaceWeaverSendMessageResponse(result.MessageId, result.AssistantMessageId, result.SessionStatus));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/sessions/{sessionId:guid}/events", async (
            Guid workspaceId,
            Guid sessionId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            try
            {
                await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken);
                return Results.Ok(new[] { new WorkspaceWeaverEventResponse("session.idle", null) });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/sessions/{sessionId:guid}/cancel", async (
            Guid workspaceId,
            Guid sessionId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverSessionService weaver,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            try
            {
                await weaver.GetSessionDetailAsync(workspaceId, sessionId, cancellationToken);
                return Results.Accepted();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/plans/{planId:guid}", async (
            Guid workspaceId,
            Guid planId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            IWeaverSessionStore store,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var plan = await store.GetPlanAsync(workspaceId, planId, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(ToPlanResponse(plan));
        });

        group.MapPost("/plans/{planId:guid}/approvals", async (
            Guid workspaceId,
            Guid planId,
            WorkspaceWeaverPlanApprovalRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverPlanExecutionService execution,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return Results.Forbid();

            try
            {
                var plan = await execution.RecordApprovalAsync(
                    workspaceId,
                    planId,
                    request.Version,
                    access.Access.AccountId,
                    request.Decision,
                    request.ConfirmationId,
                    request.Reason,
                    cancellationToken);
                return Results.Ok(new WorkspaceWeaverPlanApprovalResponse(plan.Id, plan.Version, plan.Status));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/plans/{planId:guid}/execute", async (
            Guid workspaceId,
            Guid planId,
            WorkspaceWeaverPlanExecuteRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WeaverPlanExecutionService execution,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return Results.Forbid();

            try
            {
                var result = await execution.ExecuteAsync(workspaceId, planId, request.Version, cancellationToken);
                return Results.Ok(new WorkspaceWeaverPlanExecuteResponse(result.Id, result.Status, result.LinkedResourceJson));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

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
