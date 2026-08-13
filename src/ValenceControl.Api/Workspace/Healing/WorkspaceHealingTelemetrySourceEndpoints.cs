using ValenceControl.Api.Authentication;
using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.OpenTelemetry;
using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Workspace.Healing;

public sealed class WorkspaceHealingTelemetrySourceEndpointModule : IHealingEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
            "/api/workspaces/{workspaceId:guid}/healing/applications/{applicationId:guid}/environments/{environmentId:guid}/opentelemetry/sources");
        group.AddEndpointFilter(RequireApplicationEnvironmentAsync);
        group.MapGet("/", ListAsync).RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/", CreateAsync).RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/{sourceId:guid}/rotate", RotateAsync).RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/{sourceId:guid}/revoke", RevokeAsync).RequireHealingPermission(HealingPermissions.Configure);
    }

    private static async ValueTask<object?> RequireApplicationEnvironmentAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(context, WorkspaceOperation.Read);
        if (denied is not null)
            return denied;
        if (!TryGetScope(context, out var workspaceId, out var applicationId, out var environmentId))
            return NotFound(context);

        var cockpit = context.RequestServices.GetRequiredService<DeploymentCockpitService>();
        var application = (await cockpit.GetCockpitAsync(workspaceId, context.RequestAborted)).Applications
            .SingleOrDefault(candidate => Guid.TryParse(candidate.Id, out var id) && id == applicationId);
        var hasEnvironment = application?.Environments.Any(candidate =>
            Guid.TryParse(candidate.Id, out var id) && id == environmentId) == true;
        return hasEnvironment ? await next(invocation) : NotFound(context);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        HealingTelemetrySourceService service,
        CancellationToken cancellationToken) =>
        Results.Ok((await service.ListAsync(workspaceId, applicationId, environmentId, cancellationToken))
            .Select(ToResponse));

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CreateHealingTelemetrySourceRequest request,
        HttpContext context,
        HealingTelemetrySourceService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(
                workspaceId, applicationId, environmentId, request.Name,
                ActorId(context), Guid.NewGuid(), cancellationToken);
            return Results.Created(
                $"/api/workspaces/{workspaceId:D}/healing/applications/{applicationId:D}/environments/{environmentId:D}/opentelemetry/sources/{result.Source.Id:D}",
                CredentialResponse(result));
        }
        catch (ArgumentException exception)
        {
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status400BadRequest, "healing.telemetry-source.invalid", exception.Message);
        }
    }

    private static async Task<IResult> RotateAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        HttpContext context,
        HealingTelemetrySourceService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RotateAsync(
                workspaceId, applicationId, environmentId, sourceId,
                ActorId(context), Guid.NewGuid(), cancellationToken);
            return result is null
                ? SourceNotFound(context)
                : Results.Ok(CredentialResponse(result));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentMutation(context);
        }
    }

    private static async Task<IResult> RevokeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        HttpContext context,
        HealingTelemetrySourceService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await service.RevokeAsync(
                workspaceId, applicationId, environmentId, sourceId,
                ActorId(context), Guid.NewGuid(), cancellationToken);
            return source is null ? SourceNotFound(context) : Results.Ok(ToResponse(source));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentMutation(context);
        }
    }

    private static HealingTelemetrySourceCredentialResponse CredentialResponse(
        HealingTelemetrySourceCredentialResult result) =>
        new(ToResponse(result.Source), result.Token, HealingTelemetrySourceTokenService.HeaderName);

    private static HealingTelemetrySourceResponse ToResponse(HealingTelemetrySource source) => new(
        source.Id,
        source.Name,
        source.Status.ToString(),
        source.CredentialVersion,
        source.CreatedAt,
        source.RotatedAt,
        source.RevokedAt,
        Convert.ToBase64String(source.Version));

    private static bool TryGetScope(
        HttpContext context,
        out Guid workspaceId,
        out Guid applicationId,
        out Guid environmentId)
    {
        workspaceId = Guid.Empty;
        applicationId = Guid.Empty;
        environmentId = Guid.Empty;
        return Guid.TryParse(context.Request.RouteValues["workspaceId"]?.ToString(), out workspaceId) &&
               Guid.TryParse(context.Request.RouteValues["applicationId"]?.ToString(), out applicationId) &&
               Guid.TryParse(context.Request.RouteValues["environmentId"]?.ToString(), out environmentId);
    }

    private static IResult NotFound(HttpContext context) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(
            context, StatusCodes.Status404NotFound, "healing.environment.not-found", "Application environment was not found.");

    private static IResult SourceNotFound(HttpContext context) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(
            context, StatusCodes.Status404NotFound, "healing.telemetry-source.not-found", "Telemetry source was not found.");

    private static IResult ConcurrentMutation(HttpContext context) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(
            context, StatusCodes.Status409Conflict, "healing.telemetry-source.stale", "Telemetry source changed after it was loaded.");

    private static string ActorId(HttpContext context) => context.GetWorkspaceAccess().AccountId.ToString("D");
}

public sealed record CreateHealingTelemetrySourceRequest(string Name);

public sealed record HealingTelemetrySourceResponse(
    Guid Id,
    string Name,
    string Status,
    int CredentialVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RevokedAt,
    string Version);

public sealed record HealingTelemetrySourceCredentialResponse(
    HealingTelemetrySourceResponse Source,
    string Token,
    string HeaderName);
