using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceArtifactEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceArtifactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/artifacts")
            .WithTags("Workspace Artifacts");

        group.MapGet("/types", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            IArtifactTypeRegistry artifactTypes,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            return Results.Ok(new WorkspaceArtifactTypeListResponse(artifactTypes.ListTypes()));
        });

        group.MapGet("", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            return Results.Ok(new WorkspaceArtifactListResponse(await artifacts.ListArtifactsAsync(workspaceId, cancellationToken)));
        });

        group.MapGet("/{artifactRecordId:guid}", async (
            Guid workspaceId,
            Guid artifactRecordId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            var artifact = await artifacts.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken);
            return artifact is null ? Results.NotFound() : Results.Ok(artifact);
        });

        group.MapPost("", async (
            Guid workspaceId,
            WorkspaceArtifactRegistrationRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var registration = await artifacts.RegisterArtifactAsync(
                    workspaceId,
                    new RegisterWorkspaceArtifactRequest(
                        request.ArtifactId,
                        request.LayoutVersion,
                        request.ContentDigest,
                        request.Format,
                        request.ReferenceProvider,
                        request.Reference,
                        request.Manifest,
                        request.Resources,
                        request.Diagnostics,
                        access.Access!.AccountId,
                        request.EnvelopeVersion,
                        request.ArtifactTypeId,
                        request.ArtifactSchemaVersion,
                        request.ManifestDigest,
                        request.PayloadReference,
                        request.Producer,
                        request.DisplayMetadata,
                        request.CompatibilityHints),
                    cancellationToken);
                return registration.Created
                    ? Results.Created($"/api/workspaces/{workspaceId:D}/artifacts/{registration.Artifact.Id:D}", registration.Artifact)
                    : Results.Ok(registration.Artifact);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: IsConflict(ex) ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/{artifactRecordId:guid}/refresh", async (
            Guid workspaceId,
            Guid artifactRecordId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                return Results.Ok(await artifacts.RefreshInspectionAsync(workspaceId, artifactRecordId, cancellationToken));
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

    private static bool IsConflict(InvalidOperationException exception) =>
        exception.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase);
}
