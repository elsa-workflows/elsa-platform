using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Api.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceDeploymentEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments")
            .WithTags("Workspace Deployments")
            .MapCommonApiExceptions();

        group.MapGet("/permissions", async (
            HttpContext context,
            WorkspacePermissionService permissions,
            CancellationToken cancellationToken) =>
        {
            var effective = await permissions.GetEffectiveDeploymentPermissionsAsync(context.GetWorkspaceAccess(), cancellationToken);
            return Results.Ok(new WorkspaceDeploymentPermissionsResponse(effective.Permissions.OrderBy(x => x, StringComparer.Ordinal).ToList()));
        }).RequireWorkspaceAccess();

        group.MapGet("/cockpit", async (
            Guid workspaceId,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
            Results.Ok(await cockpit.GetCockpitAsync(workspaceId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/tier-capabilities", (DeploymentTierService tiers) =>
            Results.Ok(new WorkspaceDeploymentTierCapabilitiesResponse(tiers.GetCapabilityCatalog())))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/environments/{environmentId:guid}/desired-state-requirements", async (
            Guid workspaceId,
            Guid environmentId,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
        {
            var deploymentCockpit = await cockpit.GetCockpitAsync(workspaceId, cancellationToken);
            var environment = deploymentCockpit.Applications
                .SelectMany(application => application.Environments)
                .SingleOrDefault(candidate => Guid.TryParse(candidate.Id, out var candidateId) && candidateId == environmentId);
            if (environment is null)
                return Results.NotFound();

            return Results.Ok(new WorkspaceDesiredStateRequirementsResponse(
                environmentId,
                environment.Name,
                string.IsNullOrWhiteSpace(environment.TierName) ? environment.Tier.ToString() : environment.TierName,
                DeploymentTierService.CapabilitiesFor(environment),
                DeploymentTierService.DesiredStateRequirementsFor(environment)));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/tiers", async (
            Guid workspaceId,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
            Results.Ok(new WorkspaceDeploymentTiersResponse(await tiers.ListTiersAsync(workspaceId, cancellationToken))))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/tiers", async (
            Guid workspaceId,
            WorkspaceDeploymentTierRequest request,
            HttpContext context,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var tier = await tiers.CreateTierAsync(
                workspaceId,
                new CreateDeploymentTierRequest(request.Name, request.Description, request.SortOrder, request.Capabilities, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/tiers/{tier.Id:D}", tier);
        }).RequireDeploymentOwner();

        group.MapPut("/tiers/{tierId:guid}", async (
            Guid workspaceId,
            Guid tierId,
            WorkspaceDeploymentTierRequest request,
            HttpContext context,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
            Results.Ok(await tiers.UpdateTierAsync(
                workspaceId,
                tierId,
                new UpdateDeploymentTierRequest(request.Name, request.Description, request.SortOrder, request.Capabilities, request.ImpactAccepted, context.GetWorkspaceAccess().AccountId),
                cancellationToken)))
            .RequireDeploymentOwner();

        group.MapPost("/tiers/{tierId:guid}/impact-preview", async (
            Guid workspaceId,
            Guid tierId,
            WorkspaceDeploymentTierImpactPreviewRequest request,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
            Results.Ok(await tiers.PreviewImpactAsync(workspaceId, tierId, new PreviewDeploymentTierImpactRequest(request.Capabilities), cancellationToken)))
            .RequireDeploymentOwner();

        group.MapPost("/tiers/{tierId:guid}/archive", async (
            Guid workspaceId,
            Guid tierId,
            HttpContext context,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
            Results.Ok(await tiers.ArchiveTierAsync(workspaceId, tierId, new ArchiveDeploymentTierRequest(context.GetWorkspaceAccess().AccountId), cancellationToken)))
            .RequireDeploymentOwner();

        group.MapPost("/tiers/{tierId:guid}/restore", async (
            Guid workspaceId,
            Guid tierId,
            HttpContext context,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
            Results.Ok(await tiers.RestoreTierAsync(workspaceId, tierId, new RestoreDeploymentTierRequest(context.GetWorkspaceAccess().AccountId), cancellationToken)))
            .RequireDeploymentOwner();

        group.MapGet("/secret-stores", async (
            Guid workspaceId,
            bool? includeArchived,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var stores = await deployments.ListSecretStoresAsync(workspaceId, includeArchived == true, cancellationToken);
            return Results.Ok(new WorkspaceDeploymentSecretStoresResponse(stores));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/secret-stores", async (
            Guid workspaceId,
            WorkspaceDeploymentSecretStoreRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var store = await deployments.CreateSecretStoreAsync(
                workspaceId,
                new CreateDeploymentSecretStoreRequest(request.Name, request.Provider, request.Description, context.GetWorkspaceAccess().AccountId, request.Type),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/secret-stores/{store.Id:D}", store);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPut("/secret-stores/{secretStoreId:guid}", async (
            Guid workspaceId,
            Guid secretStoreId,
            WorkspaceDeploymentSecretStoreRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.UpdateSecretStoreAsync(
                workspaceId,
                secretStoreId,
                new UpdateDeploymentSecretStoreRequest(request.Name, request.Provider, request.Description, context.GetWorkspaceAccess().AccountId, request.Type),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/secret-stores/{secretStoreId:guid}/archive", async (
            Guid workspaceId,
            Guid secretStoreId,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.ArchiveSecretStoreAsync(workspaceId, secretStoreId, context.GetWorkspaceAccess().AccountId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapGet("/secret-stores/{secretStoreId:guid}/credential-references", async (
            Guid workspaceId,
            Guid secretStoreId,
            bool? includeArchived,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var references = await deployments.ListCredentialReferencesAsync(workspaceId, secretStoreId, includeArchived == true, cancellationToken);
            return Results.Ok(new WorkspaceDeploymentCredentialReferencesResponse(references));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/credential-references", async (
            Guid workspaceId,
            bool? includeArchived,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var references = await deployments.ListCredentialReferencesAsync(workspaceId, null, includeArchived == true, cancellationToken);
            return Results.Ok(new WorkspaceDeploymentCredentialReferencesResponse(references));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/secret-stores/{secretStoreId:guid}/credential-references", async (
            Guid workspaceId,
            Guid secretStoreId,
            WorkspaceDeploymentCredentialReferenceRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            CancellationToken cancellationToken) =>
        {
            var reference = await deployments.CreateCredentialReferenceAsync(
                workspaceId,
                new CreateDeploymentCredentialReferenceRequest(
                    secretStoreId,
                    request.Name,
                    request.Reference,
                    request.Description,
                    context.GetWorkspaceAccess().AccountId,
                    ProtectSecretValue(dataProtectionProvider, request.SecretValue)),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/credential-references/{reference.Id:D}", reference);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPut("/credential-references/{credentialReferenceId:guid}", async (
            Guid workspaceId,
            Guid credentialReferenceId,
            WorkspaceDeploymentCredentialReferenceRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.UpdateCredentialReferenceAsync(
                workspaceId,
                credentialReferenceId,
                new UpdateDeploymentCredentialReferenceRequest(
                    request.Name,
                    request.Reference,
                    request.Description,
                    context.GetWorkspaceAccess().AccountId,
                    ProtectSecretValue(dataProtectionProvider, request.SecretValue)),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/credential-references/{credentialReferenceId:guid}/rotate", async (
            Guid workspaceId,
            Guid credentialReferenceId,
            WorkspaceDeploymentCredentialReferenceRotateRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.RotateCredentialReferenceAsync(
                workspaceId,
                credentialReferenceId,
                new RotateDeploymentCredentialReferenceRequest(
                    ProtectSecretValue(dataProtectionProvider, request.SecretValue)
                        ?? throw new ArgumentException("Secret value is required.", nameof(request)),
                    context.GetWorkspaceAccess().AccountId),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapGet("/credential-references/{credentialReferenceId:guid}/usage", async (
            Guid workspaceId,
            Guid credentialReferenceId,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(new WorkspaceDeploymentCredentialReferenceUsageResponse(
                await deployments.ListCredentialReferenceUsageAsync(workspaceId, credentialReferenceId, cancellationToken))))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/credential-references/{credentialReferenceId:guid}/archive", async (
            Guid workspaceId,
            Guid credentialReferenceId,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.ArchiveCredentialReferenceAsync(workspaceId, credentialReferenceId, context.GetWorkspaceAccess().AccountId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/applications", async (
            Guid workspaceId,
            WorkspaceDeploymentApplicationRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var application = await deployments.CreateApplicationAsync(
                workspaceId,
                new CreateWorkflowApplicationRequest(request.Name, request.Description, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/applications/{application.Id:D}", application);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/applications/{applicationId:guid}/environments", async (
            Guid workspaceId,
            Guid applicationId,
            WorkspaceDeploymentEnvironmentRequest request,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var environment = await deployments.CreateEnvironmentAsync(
                workspaceId,
                new CreateDeploymentEnvironmentRequest(applicationId, request.Name, request.Tier ?? EnvironmentTier.Production, request.TierId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/environments/{environment.Id:D}", environment);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPut("/applications/{applicationId:guid}", async (
            Guid workspaceId,
            Guid applicationId,
            WorkspaceDeploymentApplicationRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.UpdateApplicationAsync(
                workspaceId,
                applicationId,
                new UpdateWorkflowApplicationRequest(request.Name, request.Description, context.GetWorkspaceAccess().AccountId),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPut("/applications/{applicationId:guid}/environments/{environmentId:guid}", async (
            Guid workspaceId,
            Guid applicationId,
            Guid environmentId,
            WorkspaceDeploymentEnvironmentRequest request,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployments.UpdateEnvironmentAsync(
                workspaceId,
                environmentId,
                new UpdateDeploymentEnvironmentRequest(applicationId, request.Name, request.Tier ?? EnvironmentTier.Production, request.TierId),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapGet("/applications/{applicationId:guid}/revisions", async (
            Guid workspaceId,
            Guid applicationId,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var revisions = await deployments.ListApplicationRevisionsAsync(workspaceId, applicationId, cancellationToken);
            return Results.Ok(new WorkspaceApplicationRevisionsResponse(revisions));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/revisions/{revisionId:guid}", async (
            Guid workspaceId,
            Guid revisionId,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var revision = await deployments.GetRevisionDetailAsync(workspaceId, revisionId, cancellationToken);
            return revision is null ? Results.NotFound() : Results.Ok(revision);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/revisions/{revisionId:guid}/deployability", async (
            Guid workspaceId,
            Guid revisionId,
            WorkspaceDeployabilityRequestDto request,
            DeploymentDeployabilityService deployability,
            CancellationToken cancellationToken) =>
            Results.Ok(await deployability.EvaluateAsync(
                workspaceId,
                revisionId,
                new DeploymentDeployabilityRequest(request.TargetEnvironmentId, request.TargetEngineId, request.Mode),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/environments/{environmentId:guid}/engines", async (
            Guid workspaceId,
            Guid environmentId,
            WorkspaceWorkflowEngineRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var engine = await deployments.RegisterEngineAsync(
                workspaceId,
                new RegisterWorkflowEngineRequest(
                    environmentId,
                    request.Name,
                    request.BaseUrl,
                    request.Region,
                    request.CredentialProvider ?? "",
                    request.CredentialReference ?? "",
                    request.Capabilities,
                    request.Controls,
                    request.HostingProvider,
                    request.CredentialReferenceId,
                    request.CredentialAssignmentStatus),
                cancellationToken);
            var healthResult = await health.VerifyEngineAsync(
                workspaceId,
                new EngineHealthVerificationRequest(engine.Id, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/engines/{engine.Id:D}", ApplyHealthResult(engine, healthResult));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPut("/engines/{engineId:guid}", async (
            Guid workspaceId,
            Guid engineId,
            WorkspaceWorkflowEngineRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var engine = await deployments.UpdateEngineAsync(
                workspaceId,
                engineId,
                new UpdateWorkflowEngineRequest(
                    request.Name,
                    request.BaseUrl,
                    request.Region,
                    request.CredentialProvider ?? string.Empty,
                    request.CredentialReference ?? string.Empty,
                    request.Capabilities,
                    request.Controls,
                    request.HostingProvider,
                    request.CredentialReferenceId,
                    request.CredentialAssignmentStatus),
                cancellationToken);
            var healthResult = await health.VerifyEngineAsync(
                workspaceId,
                new EngineHealthVerificationRequest(engine.Id, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Ok(ApplyHealthResult(engine, healthResult));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/engines/{engineId:guid}/verify", async (
            Guid workspaceId,
            Guid engineId,
            HttpContext context,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
            Results.Ok(await health.VerifyEngineAsync(
                workspaceId,
                new EngineHealthVerificationRequest(engineId, context.GetWorkspaceAccess().AccountId),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/engines/{engineId:guid}/heartbeat", async (
            Guid workspaceId,
            Guid engineId,
            WorkspaceEngineHeartbeatRequest request,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
            Results.Ok(await health.ApplyHeartbeatAsync(
                workspaceId,
                new EngineHeartbeatRequest(
                    engineId,
                    request.EnvironmentId,
                    request.Version,
                    request.CertificateStatus,
                    request.CredentialVerificationStatus,
                    request.HeartbeatAt,
                    request.Capabilities,
                    request.Message),
                cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/applications/{applicationId:guid}/environments/{environmentId:guid}/revisions", async (
            Guid workspaceId,
            Guid applicationId,
            Guid environmentId,
            WorkspaceDesiredStateRevisionRequest request,
            HttpContext context,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var desiredStateJson = SerializeDesiredState(request.Records);
            var revision = await deployments.CreateRevisionAsync(
                workspaceId,
                new CreateDesiredStateRevisionRequest(applicationId, environmentId, request.Label, request.Commit, desiredStateJson, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/revisions/{revision.Id:D}", revision);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageDesiredState);

        group.MapPost("/promotions/preview", async (
            Guid workspaceId,
            WorkspacePromotionPreviewRequestDto request,
            DeploymentValidationService validation,
            CancellationToken cancellationToken) =>
        {
            var comparison = await validation.PreviewPromotionAsync(
                workspaceId,
                new WorkspacePromotionPreviewRequest(request.SourceEnvironmentId, request.TargetEnvironmentId, request.SourceRevisionId, request.TargetEngineId),
                cancellationToken);
            return Results.Ok(comparison);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.PreviewPromotion);

        group.MapPost("/promotions", async (
            Guid workspaceId,
            WorkspacePromotionRequestDto request,
            HttpContext context,
            DeploymentPromotionService promotions,
            CancellationToken cancellationToken) =>
        {
            var promotion = await promotions.PromoteAsync(
                workspaceId,
                new WorkspacePromotionRequest(
                    request.SourceEnvironmentId,
                    request.TargetEnvironmentId,
                    request.SourceRevisionId,
                    request.TargetEngineId,
                    request.Label,
                    request.Commit,
                    context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/revisions/{promotion.TargetRevision.Id:D}", promotion);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageDesiredState);

        group.MapPost("/confirmations", async (
            Guid workspaceId,
            WorkspaceActionConfirmationRequest request,
            HttpContext context,
            WorkspacePermissionService permissions,
            ConfirmationService confirmations,
            CancellationToken cancellationToken) =>
        {
            var access = context.GetWorkspaceAccess();
            if (!await permissions.HasDeploymentPermissionAsync(access, PermissionForConfirmation(request.ActionType), cancellationToken))
                return DeploymentPermissionEndpointFilters.DeploymentPermissionDenied();

            var confirmation = await confirmations.CreateConfirmationAsync(
                workspaceId,
                new CreateActionConfirmationRequest(
                    request.ActionType,
                    request.TargetId,
                    access.AccountId,
                    request.LifetimeSeconds.HasValue ? TimeSpan.FromSeconds(request.LifetimeSeconds.Value) : null),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/confirmations/{confirmation.Id:D}", confirmation);
        }).RequireWorkspaceAccess();

        group.MapPost("/runs", async (
            Guid workspaceId,
            WorkspaceDeploymentRunRequestDto request,
            HttpContext context,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var run = await runs.QueueDeploymentAsync(
                workspaceId,
                new WorkspaceDeploymentRunRequest(request.SourceRevisionId, request.TargetEnvironmentId, request.TargetEngineId, context.GetWorkspaceAccess().AccountId, request.Mode),
                request.ConfirmationId,
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/runs/{run.Id:D}", run);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ExecuteDeployment);

        group.MapPost("/rollbacks", async (
            Guid workspaceId,
            WorkspaceRollbackRunRequestDto request,
            HttpContext context,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var run = await runs.QueueRollbackAsync(
                workspaceId,
                new WorkspaceDeploymentRunRequest(request.SourceRevisionId, request.TargetEnvironmentId, request.TargetEngineId, context.GetWorkspaceAccess().AccountId, request.Mode),
                request.ConfirmationId,
                request.RollbackSourceRunId,
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/runs/{run.Id:D}", run);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ExecuteRollback);

        group.MapGet("/runs/{runId:guid}", async (
            Guid workspaceId,
            Guid runId,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var detail = await runs.GetRunDetailAsync(workspaceId, runId, cancellationToken);
            return detail is null
                ? Results.NotFound()
                : Results.Ok(new WorkspaceDeploymentRunDetailResponse(detail.Run, detail.History, detail.Commands));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("/engines/{engineId:guid}/controls/{controlId}/run", async (
            Guid workspaceId,
            Guid engineId,
            string controlId,
            WorkspaceRuntimeControlRunRequest request,
            HttpContext context,
            RuntimeControlService controls,
            CancellationToken cancellationToken) =>
        {
            var execution = await controls.ExecuteControlAsync(
                workspaceId,
                new RuntimeControlExecutionRequest(engineId, controlId, request.ConfirmationId, context.GetWorkspaceAccess().AccountId),
                cancellationToken);
            return Results.Ok(execution);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ExecuteControls);

        return endpoints;
    }

    private static string SerializeDesiredState(IReadOnlyList<WorkspaceDesiredStateRecordRequest> records)
    {
        var items = new JsonArray();
        foreach (var record in records)
        {
            var payload = record.Payload.ValueKind is JsonValueKind.Undefined
                ? new JsonObject()
                : JsonNode.Parse(record.Payload.GetRawText()) ?? new JsonObject();

            items.Add(new JsonObject
            {
                ["kind"] = record.Kind.ToString(),
                ["name"] = record.Name,
                ["payload"] = payload
            });
        }

        return new JsonObject { ["records"] = items }.ToJsonString();
    }

    private static string? ProtectSecretValue(IDataProtectionProvider dataProtectionProvider, string? secretValue)
    {
        if (string.IsNullOrWhiteSpace(secretValue))
            return null;

        return dataProtectionProvider
            .CreateProtector("Elsa.Platform.EngineCredentialReferences")
            .Protect(secretValue);
    }

    private static WorkspaceWorkflowEngine ApplyHealthResult(WorkspaceWorkflowEngine engine, EngineHealthResult result) =>
        engine with
        {
            Version = result.Version,
            CertificateStatus = result.CertificateStatus,
            CredentialVerificationStatus = result.CredentialVerificationStatus,
            CredentialLastVerifiedAt = result.CredentialLastVerifiedAt,
            Health = result.Health,
            LastHeartbeatAt = result.LastHeartbeatAt,
            LastVerificationAt = result.LastVerificationAt,
            VerificationMessage = result.Message
        };

    private static string PermissionForConfirmation(ConfirmationActionType actionType) =>
        actionType switch
        {
            ConfirmationActionType.Deploy => WorkspaceDeploymentPermissions.ExecuteDeployment,
            ConfirmationActionType.Rollback => WorkspaceDeploymentPermissions.ExecuteRollback,
            ConfirmationActionType.RuntimeControl => WorkspaceDeploymentPermissions.ExecuteControls,
            _ => WorkspaceDeploymentPermissions.Read
        };
}
