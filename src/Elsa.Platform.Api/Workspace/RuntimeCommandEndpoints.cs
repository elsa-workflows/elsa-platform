using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

public static class RuntimeCommandEndpoints
{
    public static IEndpointRouteBuilder MapRuntimeCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments/runtime")
            .WithTags("Runtime Deployment Commands");

        group.MapGet("/engines/{engineId:guid}/commands", async (
            Guid workspaceId,
            Guid engineId,
            int? limit,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, workspaceId, cancellationToken);
            if (permission is not null)
                return permission;

            var result = await commands.PollPendingCommandsAsync(workspaceId, engineId, limit ?? 10, cancellationToken);
            return Results.Ok(new RuntimeCommandListResponse(result.Select(RuntimeCommandDto.FromCommand).ToList()));
        });

        group.MapPost("/commands/{commandId:guid}/claim", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandClaimRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, workspaceId, cancellationToken);
            if (permission is not null)
                return permission;

            try
            {
                var claim = await commands.ClaimCommandAsync(
                    workspaceId,
                    commandId,
                    new ClaimDeploymentCommandRequest(request.EngineId, request.WorkerId, TimeSpan.FromSeconds(request.LeaseSeconds)),
                    cancellationToken);
                return Results.Ok(new RuntimeCommandClaimResponse(RuntimeCommandDto.FromCommand(claim.Command), claim.LeaseToken));
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

        group.MapGet("/commands/{commandId:guid}/artifacts/{artifactRecordId:guid}/download", async (
            Guid workspaceId,
            Guid commandId,
            Guid artifactRecordId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, workspaceId, cancellationToken);
            if (permission is not null)
                return permission;

            var leaseToken = context.Request.Headers["X-Elsa-Command-Lease"].FirstOrDefault();
            var workerId = context.Request.Headers["X-Elsa-Worker-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(leaseToken) || string.IsNullOrWhiteSpace(workerId))
                return Results.Problem(title: "Command lease and worker identity are required.", statusCode: StatusCodes.Status409Conflict);

            try
            {
                var item = await commands.ValidateRuntimeArtifactDownloadAsync(workspaceId, commandId, artifactRecordId, leaseToken, workerId, cancellationToken);
                var download = await artifacts.OpenDownloadAsync(workspaceId, artifactRecordId, cancellationToken);
                context.Response.Headers["X-Elsa-Artifact-Digest-Algorithm"] = item.ContentDigest.Algorithm;
                context.Response.Headers["X-Elsa-Artifact-Digest"] = item.ContentDigest.Value;
                return Results.File(download.Content, download.ContentType, download.FileName);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/commands/{commandId:guid}/heartbeat", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandHeartbeatRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                commands,
                workspaceId,
                commandId,
                commandService => commandService.HeartbeatAsync(
                    workspaceId,
                    commandId,
                    new DeploymentCommandHeartbeatRequest(request.LeaseToken, request.WorkerId),
                    cancellationToken),
                cancellationToken));

        group.MapPost("/commands/{commandId:guid}/progress", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandProgressRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                commands,
                workspaceId,
                commandId,
                commandService => commandService.ProgressAsync(
                    workspaceId,
                    commandId,
                    new DeploymentCommandProgressRequest(request.LeaseToken, request.Status, request.PercentComplete, request.Message, request.Artifacts),
                    cancellationToken),
                cancellationToken));

        group.MapPost("/commands/{commandId:guid}/complete", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandCompleteRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                commands,
                workspaceId,
                commandId,
                commandService => commandService.CompleteAsync(
                    workspaceId,
                    commandId,
                    new CompleteDeploymentCommandRequest(
                        request.LeaseToken,
                        request.ObservedArtifactDigest,
                        request.RuntimeReference,
                        request.Diagnostics,
                        request.Artifacts),
                    cancellationToken),
                cancellationToken));

        group.MapPost("/commands/{commandId:guid}/fail", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandFailRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                commands,
                workspaceId,
                commandId,
                commandService => commandService.FailAsync(
                    workspaceId,
                    commandId,
                    new FailDeploymentCommandRequest(request.LeaseToken, request.Diagnostics, request.Artifacts),
                    cancellationToken),
                cancellationToken));

        group.MapPost("/commands/{commandId:guid}/reject", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandRejectRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                commands,
                workspaceId,
                commandId,
                commandService => commandService.RejectAsync(
                    workspaceId,
                    commandId,
                    new RejectDeploymentCommandRequest(request.LeaseToken, request.Diagnostics, request.Artifacts),
                    cancellationToken),
                cancellationToken));

        group.MapPost("/commands/{commandId:guid}/webhook-notifications", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandWebhookNotificationRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, workspaceId, cancellationToken);
            if (permission is not null)
                return permission;

            try
            {
                var notification = await commands.CreateWebhookNotificationAsync(workspaceId, request.EngineId, commandId, cancellationToken);
                return Results.Ok(RuntimeCommandWebhookNotificationResponse.FromNotification(notification));
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

    private static async Task<IResult> HandleCommandMutationAsync(
        HttpContext context,
        WorkspaceAccessResolver accessResolver,
        WorkspacePermissionService permissions,
        DeploymentCommandService commands,
        Guid workspaceId,
        Guid commandId,
        Func<DeploymentCommandService, Task<DeploymentCommand>> mutateAsync,
        CancellationToken cancellationToken)
    {
        var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, workspaceId, cancellationToken);
        if (permission is not null)
            return permission;

        try
        {
            var command = await mutateAsync(commands);
            return Results.Ok(RuntimeCommandDto.FromCommand(command));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult?> RequireRuntimeCommandAccessAsync(
        HttpContext context,
        WorkspaceAccessResolver accessResolver,
        WorkspacePermissionService permissions,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
        if (!access.Succeeded)
            return access.ToHttpResult();

        var effective = access.Access!.Role is WorkspaceRole.Owner
            ? await permissions.BootstrapOwnerPermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken)
            : await permissions.GetEffectivePermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken);
        return effective.Has(WorkspaceDeploymentPermissions.Read)
            ? null
            : Results.Problem(title: "Deployment permission is required.", statusCode: StatusCodes.Status403Forbidden);
    }
}
