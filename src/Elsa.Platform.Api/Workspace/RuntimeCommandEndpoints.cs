using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Platform.Api.Workspace;

public static class RuntimeCommandEndpoints
{
    private const string EngineSecretHeaderName = "X-Elsa-Engine-Secret";

    public static IEndpointRouteBuilder MapRuntimeCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments/runtime")
            .WithTags("Runtime Deployment Commands")
            .MapCommonApiExceptions();

        group.MapGet("/engines/{engineId:guid}/commands", async (
            Guid workspaceId,
            Guid engineId,
            int? limit,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, deployments, dataProtectionProvider, workspaceId, engineId, null, null, cancellationToken);
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, deployments, dataProtectionProvider, workspaceId, request.EngineId, null, null, cancellationToken);
            if (permission is not null)
                return permission;

            var claim = await commands.ClaimCommandAsync(
                workspaceId,
                commandId,
                new ClaimDeploymentCommandRequest(request.EngineId, request.WorkerId, TimeSpan.FromSeconds(request.LeaseSeconds)),
                cancellationToken);
            return Results.Ok(new RuntimeCommandClaimResponse(RuntimeCommandDto.FromCommand(claim.Command), claim.LeaseToken));
        });

        group.MapGet("/commands/{commandId:guid}/artifacts/{artifactRecordId:guid}/download", async (
            Guid workspaceId,
            Guid commandId,
            Guid artifactRecordId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCommandService commands,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, deployments, dataProtectionProvider, workspaceId, null, commandId, commands, cancellationToken);
            if (permission is not null)
                return permission;

            var leaseToken = context.Request.Headers["X-Elsa-Command-Lease"].FirstOrDefault();
            var workerId = context.Request.Headers["X-Elsa-Worker-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(leaseToken) || string.IsNullOrWhiteSpace(workerId))
                return Results.Problem(title: "Command lease and worker identity are required.", statusCode: StatusCodes.Status409Conflict);

            var item = await commands.ValidateRuntimeArtifactDownloadAsync(workspaceId, commandId, artifactRecordId, leaseToken, workerId, cancellationToken);
            var download = await artifacts.OpenDownloadAsync(workspaceId, artifactRecordId, cancellationToken);
            context.Response.Headers["X-Elsa-Artifact-Digest-Algorithm"] = item.ContentDigest.Algorithm;
            context.Response.Headers["X-Elsa-Artifact-Digest"] = item.ContentDigest.Value;
            return Results.File(download.Content, download.ContentType, download.FileName);
        });

        group.MapPost("/commands/{commandId:guid}/heartbeat", async (
            Guid workspaceId,
            Guid commandId,
            RuntimeCommandHeartbeatRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                deployments,
                dataProtectionProvider,
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                deployments,
                dataProtectionProvider,
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                deployments,
                dataProtectionProvider,
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                deployments,
                dataProtectionProvider,
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
            await HandleCommandMutationAsync(
                context,
                accessResolver,
                permissions,
                deployments,
                dataProtectionProvider,
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
            WorkspaceDeploymentService deployments,
            IDataProtectionProvider dataProtectionProvider,
            DeploymentCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, deployments, dataProtectionProvider, workspaceId, request.EngineId, null, null, cancellationToken);
            if (permission is not null)
                return permission;

            var notification = await commands.CreateWebhookNotificationAsync(workspaceId, request.EngineId, commandId, cancellationToken);
            return Results.Ok(RuntimeCommandWebhookNotificationResponse.FromNotification(notification));
        });

        return endpoints;
    }

    private static async Task<IResult> HandleCommandMutationAsync(
        HttpContext context,
        WorkspaceAccessResolver accessResolver,
        WorkspacePermissionService permissions,
        WorkspaceDeploymentService deployments,
        IDataProtectionProvider dataProtectionProvider,
        DeploymentCommandService commands,
        Guid workspaceId,
        Guid commandId,
        Func<DeploymentCommandService, Task<DeploymentCommand>> mutateAsync,
        CancellationToken cancellationToken)
    {
        var permission = await RequireRuntimeCommandAccessAsync(context, accessResolver, permissions, deployments, dataProtectionProvider, workspaceId, null, commandId, commands, cancellationToken);
        if (permission is not null)
            return permission;

        var command = await mutateAsync(commands);
        return Results.Ok(RuntimeCommandDto.FromCommand(command));
    }

    private static async Task<IResult?> RequireRuntimeCommandAccessAsync(
        HttpContext context,
        WorkspaceAccessResolver accessResolver,
        WorkspacePermissionService permissions,
        WorkspaceDeploymentService deployments,
        IDataProtectionProvider dataProtectionProvider,
        Guid workspaceId,
        Guid? engineId,
        Guid? commandId,
        DeploymentCommandService? commands,
        CancellationToken cancellationToken)
    {
        if (await TryAuthorizeEngineAsync(context, deployments, dataProtectionProvider, workspaceId, engineId, commandId, commands, cancellationToken))
            return null;

        var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
        if (!access.Succeeded)
            return access.ToHttpResult();

        return await permissions.HasDeploymentPermissionAsync(access.Access!, WorkspaceDeploymentPermissions.Read, cancellationToken)
            ? null
            : DeploymentPermissionEndpointFilters.DeploymentPermissionDenied();
    }

    private static async Task<bool> TryAuthorizeEngineAsync(
        HttpContext context,
        WorkspaceDeploymentService deployments,
        IDataProtectionProvider dataProtectionProvider,
        Guid workspaceId,
        Guid? engineId,
        Guid? commandId,
        DeploymentCommandService? commands,
        CancellationToken cancellationToken)
    {
        var submittedSecret = context.Request.Headers[EngineSecretHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(submittedSecret))
            return false;

        var resolvedEngineId = engineId;
        if (resolvedEngineId is null && commandId is not null && commands is not null)
        {
            var command = await commands.GetCommandAsync(workspaceId, commandId.Value, cancellationToken);
            resolvedEngineId = command?.EngineId;
        }

        if (resolvedEngineId is null || resolvedEngineId == Guid.Empty)
            return false;

        var secret = await deployments.GetEngineCredentialSecretAsync(workspaceId, resolvedEngineId.Value, cancellationToken);
        if (secret is null
            || secret.SecretStoreStatus != DeploymentSecretStoreStatus.Active
            || secret.CredentialReferenceStatus != DeploymentSecretStoreStatus.Active
            || secret.SecretStoreType != DeploymentSecretStoreType.LocalEncryptedDatabase
            || string.IsNullOrWhiteSpace(secret.ProtectedSecret))
            return false;

        string expectedSecret;
        try
        {
            expectedSecret = dataProtectionProvider
                .CreateProtector("Elsa.Platform.EngineCredentialReferences")
                .Unprotect(secret.ProtectedSecret);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }

        return FixedTimeEquals(expectedSecret, submittedSecret);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
