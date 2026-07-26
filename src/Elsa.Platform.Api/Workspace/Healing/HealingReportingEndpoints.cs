using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Reporting;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed class HealingReportingEndpointModule : IHealingEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing");
        group.MapGet("/overview", OverviewAsync).RequireHealingPermission(HealingPermissions.Read);
        group.MapGet("/audit", AuditAsync).RequireHealingPermission(HealingPermissions.Read);
        group.MapGet("/usage", UsageAsync).RequireHealingPermission(HealingPermissions.Read);
    }

    private static async Task<IResult> OverviewAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? environmentId,
        HealingIncidentStatus? status,
        IncidentSeverity? severity,
        bool? repairable,
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpContext context,
        HealingReportingService reporting,
        CancellationToken cancellationToken)
    {
        try
        {
            var overview = await reporting.GetOverviewAsync(
                new(workspaceId, applicationId, environmentId, status, severity, repairable, from, to), cancellationToken);
            var permissions = ((EffectiveWorkspacePermissions?)context.Items[HealingPermissionEndpointFilters.EffectivePermissionsItemKey])
                ?.Permissions.OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? [];
            return Results.Ok(overview with { Permissions = permissions });
        }
        catch (ArgumentException exception)
        {
            return Problem(context, "healing.reporting.query", exception.Message);
        }
    }

    private static async Task<IResult> AuditAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? incidentId,
        string? cursor,
        int? take,
        HttpContext context,
        HealingReportingService reporting,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await reporting.GetAuditAsync(
                new(workspaceId, applicationId, incidentId, cursor, take ?? 50), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Problem(context, "healing.audit.query", exception.Message);
        }
    }

    private static async Task<IResult> UsageAsync(
        Guid workspaceId,
        Guid? applicationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpContext context,
        HealingReportingService reporting,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await reporting.GetUsageAsync(
                new(workspaceId, applicationId, from, to), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Problem(context, "healing.usage.query", exception.Message);
        }
    }

    private static IResult Problem(HttpContext context, string code, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Healing reporting query is invalid.",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = context.TraceIdentifier
            });
}
