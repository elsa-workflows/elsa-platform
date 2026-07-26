using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using ValenceControl.RuntimeBuilder.Core.Builder;
using ValenceControl.RuntimeBuilder.Core.RuntimeConfigurations;

namespace ValenceControl.Api.Workspace;

public static class WorkspaceRuntimeConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceRuntimeConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/runtime-configurations")
            .WithTags("Workspace Runtime Configurations");

        group.MapGet("/", async (
            Guid workspaceId,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var items = await configurations.ListAsync(workspaceId, cancellationToken);
            return Results.Ok(items.Select(ToResponse));
        }).RequireWorkspaceAccess();

        group.MapPost("/", async (
            Guid workspaceId,
            WorkspaceRuntimeConfigurationRequest request,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var configuration = await configurations.CreateAsync(workspaceId, request.Name, request.Description, request.Intent, cancellationToken);
            return Results.Ok(ToResponse(configuration));
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapGet("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var configuration = await configurations.GetAsync(workspaceId, id, cancellationToken);
            return configuration is null ? Results.NotFound() : Results.Ok(ToResponse(configuration));
        }).RequireWorkspaceAccess();

        group.MapPut("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            WorkspaceRuntimeConfigurationRequest request,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var configuration = await configurations.UpdateAsync(workspaceId, id, request.Name, request.Description, request.Intent, cancellationToken);
            return configuration is null ? Results.NotFound() : Results.Ok(ToResponse(configuration));
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapDelete("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
            await configurations.DeleteAsync(workspaceId, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
            .RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapPost("/{id:guid}/clone", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var clone = await configurations.CloneAsync(workspaceId, id, cancellationToken);
            return clone is null ? Results.NotFound() : Results.Ok(ToResponse(clone));
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapPost("/{id:guid}/versions", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var version = await configurations.CreateVersionAsync(workspaceId, id, cancellationToken);
            return version is null ? Results.NotFound() : Results.Ok(ToResponse(version));
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapGet("/{id:guid}/versions", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var versions = await configurations.ListVersionsAsync(workspaceId, id, cancellationToken);
            return Results.Ok(versions.Select(ToResponse));
        }).RequireWorkspaceAccess();

        group.MapPost("/{id:guid}/bundle", async (
            Guid workspaceId,
            Guid id,
            RuntimeConfigurationService configurations,
            BundleGenerationService bundles,
            CancellationToken cancellationToken) =>
        {
            var configuration = await configurations.GetAsync(workspaceId, id, cancellationToken);
            if (configuration is null)
                return Results.NotFound();

            var result = await bundles.GenerateAsync(RuntimeConfigurationService.DeserializeIntent(configuration.IntentJson), workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        }).RequireWorkspaceAccess();

        return endpoints;
    }

    private static WorkspaceRuntimeConfigurationResponse ToResponse(RuntimeConfiguration configuration) =>
        new(
            configuration.Id,
            configuration.WorkspaceId,
            configuration.Name,
            configuration.Description,
            RuntimeConfigurationService.DeserializeIntent(configuration.IntentJson),
            configuration.CreatedAt,
            configuration.UpdatedAt);

    private static WorkspaceRuntimeConfigurationVersionResponse ToResponse(RuntimeConfigurationVersion version) =>
        new(
            version.Id,
            version.RuntimeConfigurationId,
            version.VersionNumber,
            version.Name,
            version.Description,
            RuntimeConfigurationService.DeserializeIntent(version.IntentJson),
            version.CreatedAt);
}

public sealed record WorkspaceRuntimeConfigurationRequest(
    string Name,
    string? Description,
    RuntimeBuilderIntent? Intent);

public sealed record WorkspaceRuntimeConfigurationResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    RuntimeBuilderIntent Intent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceRuntimeConfigurationVersionResponse(
    Guid Id,
    Guid RuntimeConfigurationId,
    int VersionNumber,
    string Name,
    string? Description,
    RuntimeBuilderIntent Intent,
    DateTimeOffset CreatedAt);
