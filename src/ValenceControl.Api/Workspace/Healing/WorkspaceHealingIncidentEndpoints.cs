using System.Text;
using System.Text.Json;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Workspace.Healing;

public sealed class WorkspaceHealingIncidentEndpointModule : IHealingEndpointModule
{
    private const int DefaultTake = 50;
    private const int MaximumTake = 100;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing/incidents");
        group.MapGet("/", ListAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapGet("/{incidentId:guid}", GetAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/{incidentId:guid}/repair/retry", RetryAsync)
            .RequireHealingPermission(HealingPermissions.RetryRepair);
        group.MapPost("/{incidentId:guid}/repair/stop", StopAsync)
            .RequireHealingPermission(HealingPermissions.StopRepair);
        group.MapPost("/{incidentId:guid}/confirmations", CreateCommandConfirmationAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/{incidentId:guid}/provider-commands/{commandId:guid}/confirmation", CreateProviderCommandConfirmationAsync)
            .RequireHealingPermission(HealingPermissions.StopRepair);
        group.MapPost("/{incidentId:guid}/provider-commands/{commandId:guid}/execute", ExecuteProviderCommandAsync)
            .RequireHealingPermission(HealingPermissions.StopRepair);
    }

    private static async Task<IResult> RetryAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext context,
        HealingDbContext dbContext,
        HumanProviderCommandService commands,
        HealingAuditService auditService,
        WorkspacePermissionService permissions,
        CancellationToken cancellationToken)
    {
        var access = context.GetWorkspaceAccess();
        var effective = await permissions.GetEffectivePermissionsAsync(workspaceId, access.AccountId, cancellationToken);
        return await ExecuteDirectCommandAsync(workspaceId, incidentId, HealingHumanCommands.Retry, null,
            access.AccountId, effective.Permissions, dbContext, commands, auditService, context, cancellationToken);
    }

    private static async Task<IResult> StopAsync(
        Guid workspaceId,
        Guid incidentId,
        HealingIncidentCommandRequest request,
        HttpContext context,
        HealingDbContext dbContext,
        HumanProviderCommandService commands,
        HealingAuditService auditService,
        WorkspacePermissionService permissions,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        var access = context.GetWorkspaceAccess();
        if (!request.ConfirmationId.HasValue)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.confirmation.required", "A target-bound confirmation is required.");
        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId, request.ConfirmationId.Value, access.AccountId,
            ConfirmationActionType.HealingRepairStop, CommandTarget(incidentId, HealingHumanCommands.Stop), cancellationToken);
        if (!confirmation.Succeeded)
            return Problem(context, StatusCodes.Status409Conflict, confirmation.Validation.Id, confirmation.Validation.Message);
        var effective = await permissions.GetEffectivePermissionsAsync(workspaceId, access.AccountId, cancellationToken);
        return await ExecuteDirectCommandAsync(workspaceId, incidentId, HealingHumanCommands.Stop, request.ConfirmationId,
            access.AccountId, effective.Permissions, dbContext, commands, auditService, context, cancellationToken);
    }

    private static async Task<IResult> CreateCommandConfirmationAsync(
        Guid workspaceId,
        Guid incidentId,
        HealingIncidentConfirmationRequest request,
        HttpContext context,
        HealingDbContext dbContext,
        WorkspacePermissionService permissions,
        ConfirmationService confirmations,
        HealingAuditService auditService,
        CancellationToken cancellationToken)
    {
        var incidentExists = await dbContext.HealingIncidents.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == incidentId, cancellationToken);
        if (!incidentExists)
            return Problem(context, StatusCodes.Status404NotFound, "healing.incident.not-found", "Incident was not found.");
        var access = context.GetWorkspaceAccess();
        var requiredPermission = request.ActionType switch
        {
            ConfirmationActionType.HealingRepairStop => HealingPermissions.StopRepair,
            ConfirmationActionType.HealingVerificationWaive => HealingPermissions.WaiveVerification,
            _ => null
        };
        if (requiredPermission is null)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.confirmation.action", "The confirmation action is not supported.");
        var effective = await permissions.GetEffectivePermissionsAsync(workspaceId, access.AccountId, cancellationToken);
        if (!effective.Has(requiredPermission))
            return HealingPermissionEndpointFilters.HealingPermissionDenied(context, requiredPermission);
        var command = request.ActionType == ConfirmationActionType.HealingRepairStop
            ? HealingHumanCommands.Stop
            : HealingHumanCommands.WaiveEnvironment;
        var confirmation = await confirmations.CreateConfirmationAsync(workspaceId,
            new CreateActionConfirmationRequest(request.ActionType, CommandTarget(incidentId, command), access.AccountId, TimeSpan.FromMinutes(5)),
            cancellationToken);
        await auditService.AppendAsync(new HealingAuditWrite(
            workspaceId,
            "incident",
            incidentId,
            "human-command-confirmation-created",
            "target-bound-confirmation-created",
            HealingActorTypes.Human,
            access.AccountId.ToString("D"),
            incidentId,
            confirmation.Id,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["operationType"] = command,
                ["status"] = "pending"
            }), cancellationToken);
        return Results.Created($"/api/workspaces/{workspaceId:D}/healing/incidents/{incidentId:D}/confirmations/{confirmation.Id:D}", confirmation);
    }

    private static async Task<IResult> ExecuteDirectCommandAsync(
        Guid workspaceId,
        Guid incidentId,
        string commandName,
        Guid? confirmationId,
        Guid accountId,
        IReadOnlySet<string> workspacePermissions,
        HealingDbContext dbContext,
        HumanProviderCommandService commands,
        HealingAuditService auditService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var incident = await dbContext.HealingIncidents.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == incidentId, cancellationToken);
        if (incident is null)
            return Problem(context, StatusCodes.Status404NotFound, "healing.incident.not-found", "Incident was not found.");
        var humanCommand = new HumanCommand
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = incident.ApplicationId,
            IncidentId = incidentId,
            IdempotencyKey = $"control:{accountId:N}:{commandName}:{Guid.NewGuid():N}",
            Command = commandName,
            ProviderActorId = accountId.ToString("D"),
            ProviderActorLogin = "control",
            ProviderPermissionSnapshotJson = "{\"source\":\"control-api\"}",
            RequestedAt = DateTimeOffset.UtcNow,
            Status = HumanCommandStatus.Pending
        };
        dbContext.HumanCommands.Add(humanCommand);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.AppendAsync(new HealingAuditWrite(
            workspaceId,
            "human-command",
            humanCommand.Id,
            "human-command-received",
            "control-api-request",
            HealingActorTypes.Human,
            accountId.ToString("D"),
            incidentId,
            confirmationId,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["operationType"] = commandName,
                ["status"] = HumanCommandStatus.Pending.ToString()
            }), cancellationToken);
        var decision = await commands.ExecuteAsync(humanCommand.Id,
            new(true, "admin", accountId, workspacePermissions, confirmationId, confirmationId.HasValue), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision.Executed ? Results.Ok(decision) : Results.Conflict(decision);
    }

    private static async Task<IResult> CreateProviderCommandConfirmationAsync(
        Guid workspaceId,
        Guid incidentId,
        Guid commandId,
        HttpContext context,
        HealingDbContext dbContext,
        ConfirmationService confirmations,
        HealingAuditService auditService,
        CancellationToken cancellationToken)
    {
        var commandExists = await dbContext.HumanCommands.AsNoTracking().AnyAsync(x =>
            x.Id == commandId && x.WorkspaceId == workspaceId && x.IncidentId == incidentId &&
            x.Command == HealingHumanCommands.Stop &&
            (x.Status == HumanCommandStatus.Pending || x.Status == HumanCommandStatus.Authorized), cancellationToken);
        if (!commandExists)
            return Problem(context, StatusCodes.Status404NotFound, "healing.provider-command.not-found", "An actionable provider stop command was not found.");
        var confirmation = await confirmations.CreateConfirmationAsync(workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.HealingRepairStop,
                ProviderCommandTarget(commandId),
                context.GetWorkspaceAccess().AccountId,
                TimeSpan.FromMinutes(5)), cancellationToken);
        await auditService.AppendAsync(new HealingAuditWrite(
            workspaceId,
            "human-command",
            commandId,
            "human-command-confirmation-created",
            "target-bound-confirmation-created",
            HealingActorTypes.Human,
            context.GetWorkspaceAccess().AccountId.ToString("D"),
            incidentId,
            confirmation.Id,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["operationType"] = HealingHumanCommands.Stop,
                ["status"] = "pending"
            }), cancellationToken);
        return Results.Created(
            $"/api/workspaces/{workspaceId:D}/healing/incidents/{incidentId:D}/provider-commands/{commandId:D}/confirmation/{confirmation.Id:D}",
            confirmation);
    }

    private static async Task<IResult> ExecuteProviderCommandAsync(
        Guid workspaceId,
        Guid incidentId,
        Guid commandId,
        HealingProviderCommandExecutionRequest request,
        HttpContext context,
        HealingDbContext dbContext,
        ConfirmationService confirmations,
        HealingHumanCommandCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var commandExists = await dbContext.HumanCommands.AsNoTracking().AnyAsync(x =>
            x.Id == commandId && x.WorkspaceId == workspaceId && x.IncidentId == incidentId &&
            x.Command == HealingHumanCommands.Stop &&
            (x.Status == HumanCommandStatus.Pending || x.Status == HumanCommandStatus.Authorized), cancellationToken);
        if (!commandExists)
            return Problem(context, StatusCodes.Status404NotFound, "healing.provider-command.not-found", "An actionable provider stop command was not found.");
        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId,
            request.ConfirmationId,
            context.GetWorkspaceAccess().AccountId,
            ConfirmationActionType.HealingRepairStop,
            ProviderCommandTarget(commandId),
            cancellationToken);
        if (!confirmation.Succeeded)
            return Problem(context, StatusCodes.Status409Conflict, confirmation.Validation.Id, confirmation.Validation.Message);
        if (!await coordinator.ExecuteAsync(commandId, request.ConfirmationId, true, cancellationToken))
            return Problem(context, StatusCodes.Status409Conflict, "healing.provider-command.changed", "The provider command is no longer actionable.");
        var result = await dbContext.HumanCommands.AsNoTracking().SingleAsync(x => x.Id == commandId, cancellationToken);
        var response = new HealingHumanCommandResponse(
            result.Id, result.Command, result.Status, result.ResultCode, result.RequestedAt, result.CompletedAt);
        return result.Status == HumanCommandStatus.Executed ? Results.Ok(response) : Results.Conflict(response);
    }

    private static string CommandTarget(Guid incidentId, string command) => $"healing:{command}:{incidentId:D}";
    private static string ProviderCommandTarget(Guid commandId) => $"healing:provider-command:{commandId:D}";

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? environmentId,
        HealingIncidentStatus? status,
        IncidentSeverity? severity,
        bool? repairable,
        string? cursor,
        int? take,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (applicationId is not null &&
            !await ApplicationExistsAsync(cockpitService, workspaceId, applicationId.Value, cancellationToken))
        {
            return Problem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        }
        if (!TryDecodeCursor(cursor, out var offset))
            return Problem(context, StatusCodes.Status400BadRequest, "healing.incidents.cursor", "The incident cursor is invalid.");

        var pageSize = Math.Clamp(take ?? DefaultTake, 1, MaximumTake);
        var query = dbContext.HealingIncidents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationId == applicationId.Value);
        if (status is not null)
            query = query.Where(x => x.Status == status.Value);
        if (severity is not null)
            query = query.Where(x => x.Severity == severity.Value);
        if (repairable is not null)
            query = repairable.Value
                ? query.Where(x => x.SelectedBindingId != null &&
                                   x.Status != HealingIncidentStatus.ObservationOnly &&
                                   x.Status != HealingIncidentStatus.Suppressed)
                : query.Where(x => x.SelectedBindingId == null ||
                                   x.Status == HealingIncidentStatus.ObservationOnly ||
                                   x.Status == HealingIncidentStatus.Suppressed);
        if (environmentId is not null)
            query = query.Where(x => x.ActiveEpisodeId != null && dbContext.EnvironmentImpacts.Any(impact =>
                impact.WorkspaceId == workspaceId &&
                impact.ApplicationId == x.ApplicationId &&
                impact.EpisodeId == x.ActiveEpisodeId &&
                impact.EnvironmentId == environmentId.Value));
        var incidents = await query
            .OrderByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = incidents.Length > pageSize;
        var page = incidents.Take(pageSize).ToArray();
        var activeEpisodeIds = page.Where(x => x.ActiveEpisodeId is not null).Select(x => x.ActiveEpisodeId!.Value).ToArray();
        var impacts = activeEpisodeIds.Length == 0
            ? []
            : await dbContext.EnvironmentImpacts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && activeEpisodeIds.Contains(x.EpisodeId))
                .OrderBy(x => x.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var impactsByEpisode = impacts.ToLookup(x => x.EpisodeId);
        var items = page.Select(incident => ToSummary(
            incident,
            incident.ActiveEpisodeId is null ? [] : impactsByEpisode[incident.ActiveEpisodeId.Value].ToArray())).ToArray();
        var nextCursor = hasMore && page.Length > 0
            ? EncodeCursor(offset + page.Length)
            : null;
        return Results.Ok(new HealingIncidentListResponse(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext context,
        HealingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == incidentId,
            cancellationToken);
        if (incident is null)
            return Problem(context, StatusCodes.Status404NotFound, "healing.incident.not-found", "Incident was not found.");

        var episodes = await dbContext.IncidentEpisodes.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id)
            .OrderByDescending(x => x.OpenedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var episodeIds = episodes.Select(x => x.Id).ToArray();
        var impacts = episodeIds.Length == 0
            ? []
            : await dbContext.EnvironmentImpacts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ApplicationId == incident.ApplicationId &&
                            episodeIds.Contains(x.EpisodeId))
                .OrderBy(x => x.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var environmentIds = impacts.Select(x => x.EnvironmentId).Distinct().ToArray();
        var deploymentObservations = environmentIds.Length == 0
            ? []
            : await dbContext.DeploymentObservations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == incident.ApplicationId &&
                            environmentIds.Contains(x.EnvironmentId))
                .OrderByDescending(x => x.DeployedAt).ThenBy(x => x.Id).Take(500)
                .ToArrayAsync(cancellationToken);
        var verificationResults = episodeIds.Length == 0
            ? []
            : await dbContext.VerificationResults.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == incident.ApplicationId &&
                            episodeIds.Contains(x.EpisodeId))
                .OrderBy(x => x.EnvironmentId).ThenByDescending(x => x.WindowStartedAt).ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
        var occurrences = await dbContext.IncidentOccurrences.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id)
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var occurrenceIds = occurrences.Select(x => x.Id).ToArray();
        var attributions = occurrenceIds.Length == 0
            ? []
            : await dbContext.ComponentAttributions.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ApplicationId == incident.ApplicationId &&
                            occurrenceIds.Contains(x.OccurrenceId))
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
        var workItem = await dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == incident.ApplicationId &&
                        x.IncidentId == incident.Id &&
                        (incident.ActiveEpisodeId == null || x.EpisodeId == incident.ActiveEpisodeId))
            .OrderByDescending(x => x.LastObservedAt ?? x.LastProjectedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var attempts = episodeIds.Length == 0
            ? []
            : await dbContext.RepairAttempts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ApplicationId == incident.ApplicationId &&
                            episodeIds.Contains(x.EpisodeId))
                .OrderByDescending(x => x.AttemptNumber)
                .ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
        var attemptIds = attempts.Select(x => x.Id).ToArray();
        var evidenceIds = attempts.Select(x => x.EvidenceBundleId).ToArray();
        var evidence = await dbContext.EvidenceBundles.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && evidenceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var results = await dbContext.RepairResults.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && attemptIds.Contains(x.AttemptId))
            .ToDictionaryAsync(x => x.AttemptId, cancellationToken);
        var pullRequests = await dbContext.RepairPullRequests.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && attemptIds.Contains(x.AttemptId))
            .ToDictionaryAsync(x => x.AttemptId, cancellationToken);
        var evaluationIds = pullRequests.Values.Where(x => x.MergePolicyEvaluationId.HasValue)
            .Select(x => x.MergePolicyEvaluationId!.Value).ToArray();
        var evaluations = await dbContext.PolicyEvaluations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && evaluationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var humanCommands = await dbContext.HumanCommands.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.IncidentId == incidentId)
            .OrderByDescending(x => x.RequestedAt).Take(50).ToArrayAsync(cancellationToken);
        var effective = context.Items[HealingPermissionEndpointFilters.EffectivePermissionsItemKey] as EffectiveWorkspacePermissions;

        return Results.Ok(new HealingIncidentDetailResponse(
            incident.Id,
            incident.ApplicationId,
            incident.Status,
            incident.Severity,
            incident.Classification,
            incident.FirstSeenAt,
            incident.LastSeenAt,
            incident.OccurrenceCount,
            incident.ActiveEpisodeId,
            incident.SelectedBindingId is not null,
            incident.NeedsHumanReason,
            incident.ReadyAfter,
            episodes.Select(ToEpisode).ToArray(),
            impacts.Select(ToEnvironmentImpact).ToArray(),
            deploymentObservations.Select(x => new HealingDeploymentObservationResponse(
                x.Id, x.EnvironmentId, x.Revision, x.DeployedAt, x.Source, x.SourceObservationId, x.AcceptedAt)).ToArray(),
            verificationResults.Select(x => new HealingVerificationResultResponse(
                x.Id, x.EpisodeId, x.EnvironmentId, x.RepairedRevision, x.WindowStartedAt, x.WindowEndsAt,
                x.RelevantOperationSuccessCount, x.LastRelevantOperationSuccessAt, x.RecurrenceCount,
                x.LastRecurrenceAt, x.Outcome, x.DecidedAt, x.SafeDecisionReason, x.WaiverExpiresAt)).ToArray(),
            occurrences.Select(ToOccurrence).ToArray(),
            attributions.Select(ToAttribution).ToArray(),
            workItem is null ? null : ToWorkItem(workItem),
            attempts.Select(attempt => ToRepairAttempt(
                attempt,
                evidence.GetValueOrDefault(attempt.EvidenceBundleId),
                results.GetValueOrDefault(attempt.Id),
                pullRequests.GetValueOrDefault(attempt.Id),
                pullRequests.GetValueOrDefault(attempt.Id)?.MergePolicyEvaluationId is { } evaluationId
                    ? evaluations.GetValueOrDefault(evaluationId)
                    : null)).ToArray(),
            humanCommands.Select(x => new HealingHumanCommandResponse(x.Id, x.Command, x.Status, x.ResultCode, x.RequestedAt, x.CompletedAt)).ToArray(),
            effective?.Permissions.Order(StringComparer.Ordinal).ToArray() ?? []));
    }

    private static HealingIncidentSummaryResponse ToSummary(
        HealingIncident incident,
        IReadOnlyList<EnvironmentImpact> impacts) => new(
        incident.Id,
        incident.ApplicationId,
        incident.Status,
        incident.Severity,
        incident.Classification,
        incident.FirstSeenAt,
        incident.LastSeenAt,
        incident.OccurrenceCount,
        incident.ActiveEpisodeId,
        incident.SelectedBindingId is not null,
        incident.NeedsHumanReason,
        incident.ReadyAfter,
        impacts.Select(ToEnvironmentImpact).ToArray());

    private static HealingIncidentEpisodeResponse ToEpisode(IncidentEpisode episode) => new(
        episode.Id,
        episode.PreviousEpisodeId,
        episode.OpenedAt,
        episode.ClosedAt,
        ParseStringArray(episode.ProducingRevisionsJson),
        episode.TargetRevision,
        episode.Outcome,
        episode.RegressionReason);

    private static HealingEnvironmentImpactResponse ToEnvironmentImpact(EnvironmentImpact impact) => new(
        impact.EpisodeId,
        impact.EnvironmentId,
        impact.FirstSeenAt,
        impact.LastSeenAt,
        impact.OccurrenceCount,
        ParseStringArray(impact.ProducingRevisionsJson),
        impact.CurrentDeployedRevision,
        impact.VerificationStatus,
        impact.OccurrenceThreshold,
        impact.DebounceWindow,
        impact.ThresholdReachedAt,
        impact.ReadyAfter);

    private static HealingIncidentOccurrenceResponse ToOccurrence(IncidentOccurrence occurrence) => new(
        occurrence.Id,
        occurrence.EnvironmentId,
        occurrence.RevisionId,
        occurrence.OccurredAt,
        occurrence.AcceptedAt,
        occurrence.Classification,
        occurrence.Severity,
        occurrence.ExceptionType,
        occurrence.OperationName,
        occurrence.RetryState,
        occurrence.EvidenceTier);

    private static HealingComponentAttributionResponse ToAttribution(ComponentAttribution attribution) => new(
        attribution.Id,
        attribution.OccurrenceId,
        attribution.ComponentEntryId,
        attribution.BindingId,
        attribution.Confidence,
        attribution.Basis,
        attribution.Resolution,
        ParseStringArray(attribution.ReasonCodesJson));

    private static HealingWorkItemSummaryResponse ToWorkItem(RepairWorkItemProjection workItem) => new(
        workItem.Id,
        workItem.EpisodeId,
        workItem.Number,
        workItem.Url,
        workItem.ProviderState,
        workItem.ProjectionStatus,
        workItem.LastProjectedAt,
        workItem.LastObservedAt);

    private static HealingRepairAttemptResponse ToRepairAttempt(
        RepairAttempt attempt,
        EvidenceBundle? evidence,
        RepairResult? result,
        RepairPullRequest? pullRequest,
        PolicyEvaluation? mergeEvaluation)
    {
        var reproduction = ParseJson<RepairReproductionEvidence>(result?.ReproductionJson) ??
                           new RepairReproductionEvidence(false, false, "not-attempted", "No result has been received.", []);
        var validations = ParseJson<IReadOnlyList<RepairValidationResult>>(result?.ValidationJson) ?? [];
        var risk = ParseJson<HealingRepairRiskProjection>(result?.RiskJson);
        return new HealingRepairAttemptResponse(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Status,
            attempt.TargetRevision,
            attempt.ProducingRevision,
            new HealingRepairEvidenceResponse(
                evidence?.Tier.ToString() ?? "Unavailable",
                ParseStringArray(evidence?.OmissionsJson ?? "[]"),
                evidence?.ExpiresAt),
            attempt.RepairClassification,
            result?.Confidence,
            risk?.CausalSummary,
            new HealingRepairReproductionResponse(
                reproduction.WasAttempted,
                reproduction.WasReproduced,
                reproduction.Classification,
                reproduction.Summary),
            validations.Select(x => new HealingRepairValidationResponse(x.Kind, x.Outcome, x.SafeSummary)).ToArray(),
            pullRequest is null ? null : new HealingRepairPullRequestResponse(
                pullRequest.Number,
                pullRequest.Url,
                pullRequest.IsDraft,
                pullRequest.MergeState,
                CheckState(pullRequest.CheckSnapshotJson),
                mergeEvaluation?.Decision.ToString() ?? "NotEvaluated",
                ParseMergeGates(mergeEvaluation?.GateResultsJson)));
    }

    private static IReadOnlyList<HealingMergeGateResponse> ParseMergeGates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind != JsonValueKind.Array ? [] : document.RootElement.EnumerateArray()
                .Select(x => new HealingMergeGateResponse(
                    x.TryGetProperty("Gate", out var gate) ? gate.GetString() ?? "unknown" : "unknown",
                    x.TryGetProperty("State", out var state) ? GateState(state) : "Unknown",
                    x.TryGetProperty("ReasonCode", out var reason) ? reason.GetString() ?? "unknown" : "unknown"))
                .ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string GateState(JsonElement state) =>
        state.ValueKind == JsonValueKind.String ? state.GetString() ?? "Unknown" :
        state.TryGetInt32(out var numeric) && Enum.IsDefined(typeof(ValenceControl.Healing.Abstractions.PolicyGateState), numeric)
            ? ((ValenceControl.Healing.Abstractions.PolicyGateState)numeric).ToString()
            : "Unknown";

    private static T? ParseJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
    }

    private static string CheckState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                ? state.GetString() ?? "NotReported"
                : "NotReported";
        }
        catch (JsonException)
        {
            return "NotReported";
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<bool> ApplicationExistsAsync(
        DeploymentCockpitService service,
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken) =>
        (await service.GetCockpitAsync(workspaceId, cancellationToken)).Applications.Any(x =>
            Guid.TryParse(x.Id, out var id) && id == applicationId);

    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor))
            return true;
        try
        {
            return int.TryParse(
                       Encoding.UTF8.GetString(Convert.FromBase64String(cursor)),
                       System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(context, status, code, detail);
}

public sealed record HealingIncidentListResponse(
    IReadOnlyList<HealingIncidentSummaryResponse> Items,
    string? NextCursor);

public sealed record HealingIncidentCommandRequest(Guid? ConfirmationId);
public sealed record HealingProviderCommandExecutionRequest(Guid ConfirmationId);
public sealed record HealingIncidentConfirmationRequest(ConfirmationActionType ActionType);

public sealed record HealingIncidentSummaryResponse(
    Guid Id,
    Guid ApplicationId,
    HealingIncidentStatus Status,
    IncidentSeverity Severity,
    IncidentClassification Classification,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    Guid? ActiveEpisodeId,
    bool Repairable,
    NeedsHumanReason? NeedsHumanReason,
    DateTimeOffset? ReadyAfter,
    IReadOnlyList<HealingEnvironmentImpactResponse> EnvironmentImpacts);

public sealed record HealingIncidentDetailResponse(
    Guid Id,
    Guid ApplicationId,
    HealingIncidentStatus Status,
    IncidentSeverity Severity,
    IncidentClassification Classification,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    Guid? ActiveEpisodeId,
    bool Repairable,
    NeedsHumanReason? NeedsHumanReason,
    DateTimeOffset? ReadyAfter,
    IReadOnlyList<HealingIncidentEpisodeResponse> Episodes,
    IReadOnlyList<HealingEnvironmentImpactResponse> EnvironmentImpacts,
    IReadOnlyList<HealingDeploymentObservationResponse> DeploymentObservations,
    IReadOnlyList<HealingVerificationResultResponse> VerificationResults,
    IReadOnlyList<HealingIncidentOccurrenceResponse> Occurrences,
    IReadOnlyList<HealingComponentAttributionResponse> Attributions,
    HealingWorkItemSummaryResponse? WorkItem,
    IReadOnlyList<HealingRepairAttemptResponse> Attempts,
    IReadOnlyList<HealingHumanCommandResponse> HumanCommands,
    IReadOnlyList<string> Permissions);

public sealed record HealingDeploymentObservationResponse(
    Guid Id,
    Guid EnvironmentId,
    string Revision,
    DateTimeOffset DeployedAt,
    DeploymentObservationSource Source,
    string SourceObservationId,
    DateTimeOffset AcceptedAt);

public sealed record HealingVerificationResultResponse(
    Guid Id,
    Guid EpisodeId,
    Guid EnvironmentId,
    string RepairedRevision,
    DateTimeOffset? WindowStartedAt,
    DateTimeOffset? WindowEndsAt,
    long RelevantOperationSuccessCount,
    DateTimeOffset? LastRelevantOperationSuccessAt,
    long RecurrenceCount,
    DateTimeOffset? LastRecurrenceAt,
    VerificationOutcome Outcome,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    DateTimeOffset? WaiverExpiresAt);

public sealed record HealingRepairAttemptResponse(
    Guid Id,
    int AttemptNumber,
    RepairAttemptStatus Status,
    string TargetRevision,
    string? ProducingRevision,
    HealingRepairEvidenceResponse Evidence,
    RepairClassification Classification,
    decimal? Confidence,
    string? CausalSummary,
    HealingRepairReproductionResponse Reproduction,
    IReadOnlyList<HealingRepairValidationResponse> Validations,
    HealingRepairPullRequestResponse? PullRequest);

public sealed record HealingRepairEvidenceResponse(string Tier, IReadOnlyList<string> OmittedFields, DateTimeOffset? ExpiresAt);
public sealed record HealingRepairReproductionResponse(bool WasAttempted, bool WasReproduced, string Classification, string Summary);
public sealed record HealingRepairValidationResponse(string Kind, string Outcome, string SafeSummary);
public sealed record HealingRepairPullRequestResponse(
    long Number,
    string Url,
    bool IsDraft,
    PullRequestMergeState MergeState,
    string ChecksState,
    string AutoMergeDecision,
    IReadOnlyList<HealingMergeGateResponse> MergeGates);
public sealed record HealingMergeGateResponse(string Gate, string State, string ReasonCode);
public sealed record HealingHumanCommandResponse(Guid Id, string Command, HumanCommandStatus Status, string? ResultCode, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt);
internal sealed record HealingRepairRiskProjection(string CausalSummary);

public sealed record HealingIncidentEpisodeResponse(
    Guid Id,
    Guid? PreviousEpisodeId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<string> ProducingRevisions,
    string? TargetRevision,
    IncidentEpisodeOutcome Outcome,
    string? RegressionReason);

public sealed record HealingEnvironmentImpactResponse(
    Guid EpisodeId,
    Guid EnvironmentId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long OccurrenceCount,
    IReadOnlyList<string> ProducingRevisions,
    string? CurrentDeployedRevision,
    VerificationOutcome VerificationStatus,
    int OccurrenceThreshold,
    TimeSpan DebounceWindow,
    DateTimeOffset? ThresholdReachedAt,
    DateTimeOffset? ReadyAfter);

public sealed record HealingIncidentOccurrenceResponse(
    Guid Id,
    Guid EnvironmentId,
    Guid? RevisionId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AcceptedAt,
    IncidentClassification Classification,
    IncidentSeverity Severity,
    string ExceptionType,
    string OperationName,
    IncidentRetryState RetryState,
    EvidenceTier EvidenceTier);

public sealed record HealingComponentAttributionResponse(
    Guid Id,
    Guid OccurrenceId,
    Guid ComponentEntryId,
    Guid? BindingId,
    decimal Confidence,
    AttributionBasis Basis,
    AttributionResolution Resolution,
    IReadOnlyList<string> ReasonCodes);

public sealed record HealingWorkItemSummaryResponse(
    Guid Id,
    Guid EpisodeId,
    long? Number,
    string? Url,
    string? ProviderState,
    WorkItemProjectionStatus ProjectionStatus,
    DateTimeOffset? LastProjectedAt,
    DateTimeOffset? LastObservedAt);
