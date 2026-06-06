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
        var uploadGroup = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/artifact-uploads")
            .WithTags("Workspace Artifact Uploads");

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

        group.MapGet("/{artifactRecordId:guid}/download", async (
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

            try
            {
                var download = await artifacts.OpenDownloadAsync(workspaceId, artifactRecordId, cancellationToken);
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

        uploadGroup.MapGet("/capabilities", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.Read, cancellationToken))
                return DeploymentPermissionDenied();

            var capabilities = uploads.GetCapabilities();
            return Results.Ok(new WorkspaceArtifactUploadCapabilitiesResponse(capabilities.MaxUploadBytes, capabilities.SampleArtifactGenerationEnabled));
        });

        uploadGroup.MapPost("", async (
            Guid workspaceId,
            CreateArtifactUploadRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var created = await uploads.CreateSessionAsync(workspaceId, request, access.Access!.AccountId, cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/artifact-uploads/{created.UploadId:D}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        uploadGroup.MapPut("/{uploadId:guid}/content", async (
            Guid workspaceId,
            Guid uploadId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                await uploads.UploadContentAsync(workspaceId, uploadId, context.Request.Body, context.Request.ContentLength, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: ex.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status409Conflict);
            }
        });

        uploadGroup.MapPost("/{uploadId:guid}/complete", async (
            Guid workspaceId,
            Guid uploadId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                var completed = await uploads.CompleteAsync(workspaceId, uploadId, access.Access!.AccountId, cancellationToken);
                return completed.Artifact is not null && completed.Created
                    ? Results.Created($"/api/workspaces/{workspaceId:D}/artifacts/{completed.Artifact.Id:D}", completed)
                    : Results.Ok(completed);
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

        uploadGroup.MapDelete("/{uploadId:guid}", async (
            Guid workspaceId,
            Guid uploadId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();

            try
            {
                await uploads.AbortAsync(workspaceId, uploadId, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        uploadGroup.MapPost("/dev-sample", async (
            Guid workspaceId,
            CreateSampleArtifactRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (!await HasDeploymentPermissionAsync(access.Access!, permissions, workspaceId, WorkspaceDeploymentPermissions.ManageSetup, cancellationToken))
                return DeploymentPermissionDenied();
            if (!uploads.GetCapabilities().SampleArtifactGenerationEnabled)
                return Results.NotFound();

            try
            {
                var completed = await uploads.CreateSampleArtifactAsync(workspaceId, request, access.Access!.AccountId, cancellationToken);
                return completed.Artifact is not null && completed.Created
                    ? Results.Created($"/api/workspaces/{workspaceId:D}/artifacts/{completed.Artifact.Id:D}", completed)
                    : Results.Ok(completed);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
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
