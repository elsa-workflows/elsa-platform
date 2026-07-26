using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Verification;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed class HealingVerificationEndpointModule : IHealingEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var deployments = endpoints.MapGroup(
            "/api/workspaces/{workspaceId:guid}/healing/applications/{applicationId:guid}/environments/{environmentId:guid}");
        deployments.MapPost("/deployment-observations", AppendDeploymentObservationAsync)
            .RequireHealingPermission(HealingPermissions.ReportDeployment);

        var incidents = endpoints.MapGroup(
            "/api/workspaces/{workspaceId:guid}/healing/incidents/{incidentId:guid}/environments/{environmentId:guid}");
        incidents.MapPost("/waiver-confirmations", CreateWaiverConfirmationAsync)
            .RequireHealingPermission(HealingPermissions.WaiveVerification);
        incidents.MapPost("/waive", WaiveAsync)
            .RequireHealingPermission(HealingPermissions.WaiveVerification);
    }

    private static async Task<IResult> AppendDeploymentObservationAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        HealingDeploymentObservationApiRequest request,
        HttpContext context,
        DeploymentCockpitService cockpit,
        HealingDbContext dbContext,
        DeploymentObservationService service,
        CancellationToken cancellationToken)
    {
        var application = (await cockpit.GetCockpitAsync(workspaceId, cancellationToken)).Applications
            .SingleOrDefault(x => Guid.TryParse(x.Id, out var id) && id == applicationId);
        if (application?.Environments.Any(x => Guid.TryParse(x.Id, out var id) && id == environmentId) != true)
            return Problem(context, StatusCodes.Status404NotFound, "healing.environment.not-found", "Application environment was not found.");
        var healingConfigured = await dbContext.HealingConfigurations.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId, cancellationToken);
        if (!healingConfigured)
            return Problem(context, StatusCodes.Status409Conflict, "healing.configuration.required", "Healing must be configured before deployment observations are accepted.");
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(context, StatusCodes.Status400BadRequest, "healing.deployment.idempotency-key", "Idempotency-Key is required.");
        try
        {
            var receipt = await service.AppendAsync(new DeploymentObservationRequest(
                HealingContractVersions.DeploymentProtocol,
                workspaceId,
                applicationId,
                environmentId,
                request.Revision,
                request.DeployedAt,
                DeploymentObservationSources.ExternalDelivery,
                request.SourceObservationId,
                $"platform-account:{context.GetWorkspaceAccess().AccountId:D}",
                request.EvidenceDigest,
                idempotencyKey), cancellationToken);
            return Results.Accepted(value: receipt);
        }
        catch (ArgumentException exception)
        {
            return Problem(context, StatusCodes.Status400BadRequest, "healing.deployment.invalid", exception.Message);
        }
        catch (HealingIdempotencyConflictException)
        {
            return Problem(context, StatusCodes.Status409Conflict, "healing.deployment.idempotency-conflict", "The deployment observation key was already used for a different observation.");
        }
    }

    private static async Task<IResult> CreateWaiverConfirmationAsync(
        Guid workspaceId,
        Guid incidentId,
        Guid environmentId,
        HttpContext context,
        HealingDbContext dbContext,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        if (!await HasImpactAsync(dbContext, workspaceId, incidentId, environmentId, cancellationToken))
            return Problem(context, StatusCodes.Status404NotFound, "healing.verification.not-found", "Environment verification was not found.");
        var confirmation = await confirmations.CreateConfirmationAsync(workspaceId,
            new CreateActionConfirmationRequest(
                ConfirmationActionType.HealingVerificationWaive,
                WaiverTarget(incidentId, environmentId),
                context.GetWorkspaceAccess().AccountId,
                TimeSpan.FromMinutes(5)), cancellationToken);
        return Results.Created($"/api/workspaces/{workspaceId:D}/healing/incidents/{incidentId:D}/environments/{environmentId:D}/waiver-confirmations/{confirmation.Id:D}", confirmation);
    }

    private static async Task<IResult> WaiveAsync(
        Guid workspaceId,
        Guid incidentId,
        Guid environmentId,
        HealingVerificationWaiverRequest request,
        HttpContext context,
        HealingDbContext dbContext,
        HealingVerificationService service,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1_024)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.verification.waiver-reason", "A bounded waiver reason is required.");
        if (request.Terminal == request.ExpiresAt.HasValue)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.verification.waiver-intent", "Choose either a terminal waiver or a future expiry.");
        var accountId = context.GetWorkspaceAccess().AccountId;
        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId, request.ConfirmationId, accountId,
            ConfirmationActionType.HealingVerificationWaive,
            WaiverTarget(incidentId, environmentId), cancellationToken);
        if (!confirmation.Succeeded)
            return Problem(context, StatusCodes.Status409Conflict, confirmation.Validation.Id, confirmation.Validation.Message);

        var target = await (from incident in dbContext.HealingIncidents.AsNoTracking()
                            join result in dbContext.VerificationResults.AsNoTracking()
                                on new { incident.WorkspaceId, incident.ApplicationId, EpisodeId = incident.ActiveEpisodeId }
                                equals new { result.WorkspaceId, result.ApplicationId, EpisodeId = (Guid?)result.EpisodeId }
                            where incident.WorkspaceId == workspaceId && incident.Id == incidentId && result.EnvironmentId == environmentId
                            orderby result.WindowStartedAt descending, result.Id descending
                            select new { result.EpisodeId, result.RepairedRevision }).FirstOrDefaultAsync(cancellationToken);
        if (target is null)
            return Problem(context, StatusCodes.Status404NotFound, "healing.verification.not-found", "Active environment verification was not found.");
        var waived = await service.WaiveAsync(workspaceId, target.EpisodeId, environmentId, target.RepairedRevision,
            accountId.ToString("D"), request.Reason, request.ExpiresAt, cancellationToken);
        return waived ? Results.Ok(new { outcome = VerificationOutcome.Waived }) :
            Problem(context, StatusCodes.Status409Conflict, "healing.verification.waiver-ineligible", "The verification is already terminal or cannot be waived.");
    }

    private static Task<bool> HasImpactAsync(
        HealingDbContext dbContext,
        Guid workspaceId,
        Guid incidentId,
        Guid environmentId,
        CancellationToken cancellationToken) =>
        dbContext.HealingIncidents.AsNoTracking().AnyAsync(incident =>
            incident.WorkspaceId == workspaceId && incident.Id == incidentId && incident.ActiveEpisodeId != null &&
            dbContext.EnvironmentImpacts.Any(impact => impact.WorkspaceId == workspaceId &&
                impact.ApplicationId == incident.ApplicationId && impact.EpisodeId == incident.ActiveEpisodeId &&
                impact.EnvironmentId == environmentId), cancellationToken);

    private static string WaiverTarget(Guid incidentId, Guid environmentId) =>
        $"healing:waive-environment:{incidentId:D}:{environmentId:D}";

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: code, detail: detail, instance: context.Request.Path);
}

public sealed record HealingDeploymentObservationApiRequest(
    string Revision,
    DateTimeOffset DeployedAt,
    string SourceObservationId,
    string EvidenceDigest);

public sealed record HealingVerificationWaiverRequest(
    Guid ConfirmationId,
    string Reason,
    bool Terminal,
    DateTimeOffset? ExpiresAt);
