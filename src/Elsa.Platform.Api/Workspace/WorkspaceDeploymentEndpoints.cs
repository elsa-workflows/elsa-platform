using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceDeploymentEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments")
            .WithTags("Workspace Deployments");

        group.MapGet("/permissions", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var effectivePermissions = access.Access!.Role is WorkspaceRole.Owner
                ? (await permissions.BootstrapOwnerPermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken)).Permissions
                : (await permissions.GetEffectivePermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken)).Permissions;

            return Results.Ok(new WorkspaceDeploymentPermissionsResponse(effectivePermissions.OrderBy(x => x, StringComparer.Ordinal).ToList()));
        });

        group.MapGet("/cockpit", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            return Results.Ok(await cockpit.GetCockpitAsync(workspaceId, cancellationToken));
        });

        group.MapGet("/tier-capabilities", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            return Results.Ok(new WorkspaceDeploymentTierCapabilitiesResponse(tiers.GetCapabilityCatalog()));
        });

        group.MapGet("/environments/{environmentId:guid}/desired-state-requirements", async (
            Guid workspaceId,
            Guid environmentId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

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
        });

        group.MapGet("/tiers", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            return Results.Ok(new WorkspaceDeploymentTiersResponse(await tiers.ListTiersAsync(workspaceId, cancellationToken)));
        });

        group.MapPost("/tiers", async (
            Guid workspaceId,
            WorkspaceDeploymentTierRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return DeploymentPermissionDenied();

            try
            {
                var tier = await tiers.CreateTierAsync(
                    workspaceId,
                    new CreateDeploymentTierRequest(request.Name, request.Description, request.SortOrder, request.Capabilities, access.Access.AccountId),
                    cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/tiers/{tier.Id:D}", tier);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPut("/tiers/{tierId:guid}", async (
            Guid workspaceId,
            Guid tierId,
            WorkspaceDeploymentTierRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await tiers.UpdateTierAsync(
                    workspaceId,
                    tierId,
                    new UpdateDeploymentTierRequest(request.Name, request.Description, request.SortOrder, request.Capabilities, request.ImpactAccepted, access.Access.AccountId),
                    cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/tiers/{tierId:guid}/impact-preview", async (
            Guid workspaceId,
            Guid tierId,
            WorkspaceDeploymentTierImpactPreviewRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await tiers.PreviewImpactAsync(workspaceId, tierId, new PreviewDeploymentTierImpactRequest(request.Capabilities), cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/tiers/{tierId:guid}/archive", async (
            Guid workspaceId,
            Guid tierId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await tiers.ArchiveTierAsync(workspaceId, tierId, new ArchiveDeploymentTierRequest(access.Access.AccountId), cancellationToken));
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

        group.MapPost("/tiers/{tierId:guid}/restore", async (
            Guid workspaceId,
            Guid tierId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentTierService tiers,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (access.Access!.Role is not WorkspaceRole.Owner)
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await tiers.RestoreTierAsync(workspaceId, tierId, new RestoreDeploymentTierRequest(access.Access.AccountId), cancellationToken));
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

        group.MapPost("/applications", async (
            Guid workspaceId,
            WorkspaceDeploymentApplicationRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            var application = await deployments.CreateApplicationAsync(
                workspaceId,
                new CreateWorkflowApplicationRequest(request.Name, request.Description, access.Access!.AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/applications/{application.Id:D}", application);
        });

        group.MapPost("/applications/{applicationId:guid}/environments", async (
            Guid workspaceId,
            Guid applicationId,
            WorkspaceDeploymentEnvironmentRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var environment = await deployments.CreateEnvironmentAsync(
                    workspaceId,
                    new CreateDeploymentEnvironmentRequest(applicationId, request.Name, request.Tier ?? EnvironmentTier.Production, request.TierId),
                    cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/environments/{environment.Id:D}", environment);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPut("/applications/{applicationId:guid}", async (
            Guid workspaceId,
            Guid applicationId,
            WorkspaceDeploymentApplicationRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await deployments.UpdateApplicationAsync(
                    workspaceId,
                    applicationId,
                    new UpdateWorkflowApplicationRequest(request.Name, request.Description, access.Access!.AccountId),
                    cancellationToken));
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

        group.MapPut("/applications/{applicationId:guid}/environments/{environmentId:guid}", async (
            Guid workspaceId,
            Guid applicationId,
            Guid environmentId,
            WorkspaceDeploymentEnvironmentRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await deployments.UpdateEnvironmentAsync(
                    workspaceId,
                    environmentId,
                    new UpdateDeploymentEnvironmentRequest(applicationId, request.Name, request.Tier ?? EnvironmentTier.Production, request.TierId),
                    cancellationToken));
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

        group.MapGet("/applications/{applicationId:guid}/revisions", async (
            Guid workspaceId,
            Guid applicationId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            var revisions = await deployments.ListApplicationRevisionsAsync(workspaceId, applicationId, cancellationToken);
            return Results.Ok(new WorkspaceApplicationRevisionsResponse(revisions));
        });

        group.MapGet("/revisions/{revisionId:guid}", async (
            Guid workspaceId,
            Guid revisionId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            var revision = await deployments.GetRevisionDetailAsync(workspaceId, revisionId, cancellationToken);
            return revision is null ? Results.NotFound() : Results.Ok(revision);
        });

        group.MapPost("/environments/{environmentId:guid}/engines", async (
            Guid workspaceId,
            Guid environmentId,
            WorkspaceWorkflowEngineRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            var engine = await deployments.RegisterEngineAsync(
                workspaceId,
                new RegisterWorkflowEngineRequest(
                    environmentId,
                    request.Name,
                    request.BaseUrl,
                    request.Region,
                    request.CredentialProvider,
                    request.CredentialReference,
                    request.Capabilities,
                    request.Controls,
                    request.HostingProvider),
                cancellationToken);
            var healthResult = await health.VerifyEngineAsync(
                workspaceId,
                new EngineHealthVerificationRequest(engine.Id, access.Access!.AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/engines/{engine.Id:D}", ApplyHealthResult(engine, healthResult));
        });

        group.MapPut("/engines/{engineId:guid}", async (
            Guid workspaceId,
            Guid engineId,
            WorkspaceWorkflowEngineRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var engine = await deployments.UpdateEngineAsync(
                    workspaceId,
                    engineId,
                    new UpdateWorkflowEngineRequest(
                        request.Name,
                        request.BaseUrl,
                        request.Region,
                        request.CredentialProvider,
                        request.CredentialReference,
                        request.Capabilities,
                        request.Controls,
                        request.HostingProvider),
                    cancellationToken);
                var healthResult = await health.VerifyEngineAsync(
                    workspaceId,
                    new EngineHealthVerificationRequest(engine.Id, access.Access!.AccountId),
                    cancellationToken);
                return Results.Ok(ApplyHealthResult(engine, healthResult));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/engines/{engineId:guid}/verify", async (
            Guid workspaceId,
            Guid engineId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await health.VerifyEngineAsync(
                    workspaceId,
                    new EngineHealthVerificationRequest(engineId, access.Access!.AccountId),
                    cancellationToken));
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

        group.MapPost("/engines/{engineId:guid}/heartbeat", async (
            Guid workspaceId,
            Guid engineId,
            WorkspaceEngineHeartbeatRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            EngineHealthService health,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await health.ApplyHeartbeatAsync(
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
                    cancellationToken));
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

        group.MapPost("/applications/{applicationId:guid}/environments/{environmentId:guid}/revisions", async (
            Guid workspaceId,
            Guid applicationId,
            Guid environmentId,
            WorkspaceDesiredStateRevisionRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageDesiredState, cancellationToken))
                return DeploymentPermissionDenied();

            var desiredStateJson = SerializeDesiredState(request.Records);
            var revision = await deployments.CreateRevisionAsync(
                workspaceId,
                new CreateDesiredStateRevisionRequest(applicationId, environmentId, request.Label, request.Commit, desiredStateJson, access.Access!.AccountId),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/revisions/{revision.Id:D}", revision);
        });

        group.MapPost("/promotions/preview", async (
            Guid workspaceId,
            WorkspacePromotionPreviewRequestDto request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentValidationService validation,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.PreviewPromotion, cancellationToken))
                return DeploymentPermissionDenied();

            var comparison = await validation.PreviewPromotionAsync(
                workspaceId,
                new WorkspacePromotionPreviewRequest(request.SourceEnvironmentId, request.TargetEnvironmentId, request.SourceRevisionId, request.TargetEngineId),
                cancellationToken);
            return Results.Ok(comparison);
        });

        group.MapPost("/promotions", async (
            Guid workspaceId,
            WorkspacePromotionRequestDto request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentPromotionService promotions,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageDesiredState, cancellationToken))
                return DeploymentPermissionDenied();

            try
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
                        access.Access!.AccountId),
                    cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/revisions/{promotion.TargetRevision.Id:D}", promotion);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/confirmations", async (
            Guid workspaceId,
            WorkspaceActionConfirmationRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            ConfirmationService confirmations,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, PermissionForConfirmation(request.ActionType), cancellationToken))
                return DeploymentPermissionDenied();

            var confirmation = await confirmations.CreateConfirmationAsync(
                workspaceId,
                new CreateActionConfirmationRequest(
                    request.ActionType,
                    request.TargetId,
                    access.Access!.AccountId,
                    request.LifetimeSeconds.HasValue ? TimeSpan.FromSeconds(request.LifetimeSeconds.Value) : null),
                cancellationToken);
            return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/confirmations/{confirmation.Id:D}", confirmation);
        });

        group.MapPost("/runs", async (
            Guid workspaceId,
            WorkspaceDeploymentRunRequestDto request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ExecuteDeployment, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var run = await runs.QueueDeploymentAsync(
                    workspaceId,
                    new WorkspaceDeploymentRunRequest(request.SourceRevisionId, request.TargetEnvironmentId, request.TargetEngineId, access.Access!.AccountId, request.Mode),
                    request.ConfirmationId,
                    cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/runs/{run.Id:D}", run);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/rollbacks", async (
            Guid workspaceId,
            WorkspaceRollbackRunRequestDto request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ExecuteRollback, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var run = await runs.QueueRollbackAsync(
                    workspaceId,
                    new WorkspaceDeploymentRunRequest(request.SourceRevisionId, request.TargetEnvironmentId, request.TargetEngineId, access.Access!.AccountId, request.Mode),
                    request.ConfirmationId,
                    request.RollbackSourceRunId,
                    cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/deployments/runs/{run.Id:D}", run);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapGet("/runs/{runId:guid}", async (
            Guid workspaceId,
            Guid runId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentRunService runs,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            var detail = await runs.GetRunDetailAsync(workspaceId, runId, cancellationToken);
            return detail is null
                ? Results.NotFound()
                : Results.Ok(new WorkspaceDeploymentRunDetailResponse(detail.Run, detail.History, detail.Commands));
        });

        group.MapPost("/engines/{engineId:guid}/controls/{controlId}/run", async (
            Guid workspaceId,
            Guid engineId,
            string controlId,
            WorkspaceRuntimeControlRunRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            RuntimeControlService controls,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ExecuteControls, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var execution = await controls.ExecuteControlAsync(
                    workspaceId,
                    new RuntimeControlExecutionRequest(engineId, controlId, request.ConfirmationId, access.Access!.AccountId),
                    cancellationToken);
                return Results.Ok(execution);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

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

    private static async Task<bool> HasDeploymentPermissionAsync(
        WorkspaceAccess access,
        WorkspacePermissionService permissions,
        Guid workspaceId,
        string permission,
        CancellationToken cancellationToken)
    {
        var effective = access.Role is WorkspaceRole.Owner
            ? await permissions.BootstrapOwnerPermissionsAsync(workspaceId, access.AccountId, cancellationToken)
            : await permissions.GetEffectivePermissionsAsync(workspaceId, access.AccountId, cancellationToken);
        return effective.Has(permission);
    }

    private static IResult DeploymentPermissionDenied() =>
        Results.Problem(
            title: "Deployment permission is required.",
            statusCode: StatusCodes.Status403Forbidden);

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
