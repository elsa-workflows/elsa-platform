using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Public.Builder;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Builder;
using Elsa.Platform.PackageCatalog.Core.RuntimeConfigurations;

namespace Elsa.Platform.PackageCatalog.Api.Workspace;

public static class WorkspaceRuntimeConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceRuntimeConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/runtime-configurations")
            .WithTags("Workspace Runtime Configurations");

        group.MapGet("/", async (
            Guid workspaceId,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var items = await configurations.ListAsync(workspaceId, cancellationToken);
            return Results.Ok(items.Select(ToResponse));
        });

        group.MapPost("/", async (
            Guid workspaceId,
            WorkspaceRuntimeConfigurationRequest request,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var configuration = await configurations.CreateAsync(workspaceId, request.Name, request.Description, request.Intent, cancellationToken);
            return Results.Ok(ToResponse(configuration));
        });

        group.MapGet("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var configuration = await configurations.GetAsync(workspaceId, id, cancellationToken);
            return configuration is null ? Results.NotFound() : Results.Ok(ToResponse(configuration));
        });

        group.MapPut("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            WorkspaceRuntimeConfigurationRequest request,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var configuration = await configurations.UpdateAsync(workspaceId, id, request.Name, request.Description, request.Intent, cancellationToken);
            return configuration is null ? Results.NotFound() : Results.Ok(ToResponse(configuration));
        });

        group.MapDelete("/{id:guid}", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            return await configurations.DeleteAsync(workspaceId, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapPost("/{id:guid}/clone", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var clone = await configurations.CloneAsync(workspaceId, id, cancellationToken);
            return clone is null ? Results.NotFound() : Results.Ok(ToResponse(clone));
        });

        group.MapPost("/{id:guid}/versions", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var version = await configurations.CreateVersionAsync(workspaceId, id, cancellationToken);
            return version is null ? Results.NotFound() : Results.Ok(ToResponse(version));
        });

        group.MapGet("/{id:guid}/versions", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var versions = await configurations.ListVersionsAsync(workspaceId, id, cancellationToken);
            return Results.Ok(versions.Select(ToResponse));
        });

        group.MapPost("/{id:guid}/bundle", async (
            Guid workspaceId,
            Guid id,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            RuntimeConfigurationService configurations,
            BundleGenerationService bundles,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var configuration = await configurations.GetAsync(workspaceId, id, cancellationToken);
            if (configuration is null)
                return Results.NotFound();

            var result = await bundles.GenerateAsync(RuntimeConfigurationService.DeserializeIntent(configuration.IntentJson), workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        });

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
