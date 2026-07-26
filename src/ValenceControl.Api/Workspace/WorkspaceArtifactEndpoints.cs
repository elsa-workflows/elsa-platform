using ValenceControl.Api.Authentication;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Workspace;

namespace ValenceControl.Api.Workspace;

public static class WorkspaceArtifactEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceArtifactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/artifacts")
            .WithTags("Workspace Artifacts")
            .MapCommonApiExceptions();
        var uploadGroup = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/artifact-uploads")
            .WithTags("Workspace Artifact Uploads")
            .MapCommonApiExceptions();

        group.MapGet("/types", (IArtifactTypeRegistry artifactTypes) =>
            Results.Ok(new WorkspaceArtifactTypeListResponse(artifactTypes.ListTypes())))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("", async (
            Guid workspaceId,
            bool? includeArchived,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
            Results.Ok(new WorkspaceArtifactListResponse(await artifacts.ListArtifactsAsync(workspaceId, includeArchived ?? false, cancellationToken))))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/{artifactRecordId:guid}", async (
            Guid workspaceId,
            Guid artifactRecordId,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var artifact = await artifacts.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken);
            return artifact is null ? Results.NotFound() : Results.Ok(artifact);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapGet("/{artifactRecordId:guid}/download", async (
            Guid workspaceId,
            Guid artifactRecordId,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
            var download = await artifacts.OpenDownloadAsync(workspaceId, artifactRecordId, cancellationToken);
            return Results.File(download.Content, download.ContentType, download.FileName);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        group.MapPost("", async (
            Guid workspaceId,
            WorkspaceArtifactRegistrationRequest request,
            HttpContext context,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
        {
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
                        context.GetWorkspaceAccess().AccountId,
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
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/{artifactRecordId:guid}/refresh", async (
            Guid workspaceId,
            Guid artifactRecordId,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
            Results.Ok(await artifacts.RefreshInspectionAsync(workspaceId, artifactRecordId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/{artifactRecordId:guid}/archive", async (
            Guid workspaceId,
            Guid artifactRecordId,
            HttpContext context,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
            Results.Ok(await artifacts.ArchiveArtifactAsync(workspaceId, artifactRecordId, context.GetWorkspaceAccess().AccountId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        group.MapPost("/{artifactRecordId:guid}/restore", async (
            Guid workspaceId,
            Guid artifactRecordId,
            WorkspaceArtifactService artifacts,
            CancellationToken cancellationToken) =>
            Results.Ok(await artifacts.RestoreArtifactAsync(workspaceId, artifactRecordId, cancellationToken)))
            .RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        uploadGroup.MapGet("/capabilities", (WorkspaceArtifactUploadService uploads) =>
        {
            var capabilities = uploads.GetCapabilities();
            return Results.Ok(new WorkspaceArtifactUploadCapabilitiesResponse(capabilities.MaxUploadBytes, capabilities.SampleArtifactGenerationEnabled));
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.Read);

        uploadGroup.MapPost("", async (
            Guid workspaceId,
            CreateArtifactUploadRequest request,
            HttpContext context,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await uploads.CreateSessionAsync(workspaceId, request, context.GetWorkspaceAccess().AccountId, cancellationToken);
                return Results.Created($"/api/workspaces/{workspaceId:D}/artifact-uploads/{created.UploadId:D}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        uploadGroup.MapPut("/{uploadId:guid}/content", async (
            Guid workspaceId,
            Guid uploadId,
            HttpContext context,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await uploads.UploadContentAsync(workspaceId, uploadId, context.Request.Body, context.Request.ContentLength, cancellationToken);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: ex.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status409Conflict);
            }
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        uploadGroup.MapPost("/{uploadId:guid}/complete", async (
            Guid workspaceId,
            Guid uploadId,
            HttpContext context,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            var completed = await uploads.CompleteAsync(workspaceId, uploadId, context.GetWorkspaceAccess().AccountId, cancellationToken);
            return completed.Artifact is not null && completed.Created
                ? Results.Created($"/api/workspaces/{workspaceId:D}/artifacts/{completed.Artifact.Id:D}", completed)
                : Results.Ok(completed);
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        uploadGroup.MapDelete("/{uploadId:guid}", async (
            Guid workspaceId,
            Guid uploadId,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            await uploads.AbortAsync(workspaceId, uploadId, cancellationToken);
            return Results.NoContent();
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        uploadGroup.MapPost("/dev-sample", async (
            Guid workspaceId,
            CreateSampleArtifactRequest request,
            HttpContext context,
            WorkspaceArtifactUploadService uploads,
            CancellationToken cancellationToken) =>
        {
            if (!uploads.GetCapabilities().SampleArtifactGenerationEnabled)
                return Results.NotFound();

            try
            {
                var completed = await uploads.CreateSampleArtifactAsync(workspaceId, request, context.GetWorkspaceAccess().AccountId, cancellationToken);
                return completed.Artifact is not null && completed.Created
                    ? Results.Created($"/api/workspaces/{workspaceId:D}/artifacts/{completed.Artifact.Id:D}", completed)
                    : Results.Ok(completed);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireDeploymentPermission(WorkspaceDeploymentPermissions.ManageSetup);

        return endpoints;
    }

    private static bool IsConflict(InvalidOperationException exception) =>
        exception.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase);
}
