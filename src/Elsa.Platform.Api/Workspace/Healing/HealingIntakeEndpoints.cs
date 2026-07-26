using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed record HealingTelemetryQueryScope(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId);

/// <summary>
/// Workspace-scoped facade over the Platform OpenTelemetry capability. Implementations must resolve telemetry
/// resources from trusted collector/deployment bindings and must not trust application or environment attributes
/// supplied by a monitored process.
/// </summary>
public interface IWorkspaceHealingOpenTelemetryQuery
{
    ValueTask<CollectorConfiguration> GetCollectorConfigurationAsync(
        HealingTelemetryQueryScope scope,
        CancellationToken cancellationToken = default);

    ValueTask<OpenTelemetryLogResult> GetLogsAsync(
        HealingTelemetryQueryScope scope,
        OpenTelemetryLogFilter filter,
        CancellationToken cancellationToken = default);

    ValueTask<OpenTelemetryTraceResult> GetTracesAsync(
        HealingTelemetryQueryScope scope,
        OpenTelemetryTraceFilter filter,
        CancellationToken cancellationToken = default);

    ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
        HealingTelemetryQueryScope scope,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed class HealingIntakeEndpointModule : IHealingEndpointModule
{
    private const int MaxEnvelopeBytes = 262_144;
    private const int MaxOperationLength = 1_024;
    private const int MaxExceptionTypeLength = 1_024;
    private const int MaxMessageLength = 8_192;
    private const int MaxStackLength = 65_536;
    private const int MaxFrames = 256;
    private const int MaxIdempotencyKeyLength = 256;
    private const int MaxQueryTake = 200;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
            "/api/workspaces/{workspaceId:guid}/healing/applications/{applicationId:guid}/environments/{environmentId:guid}");
        group.AddEndpointFilter(RequireApplicationEnvironmentAsync);

        group.MapPost("/incidents", AppendExplicitIncidentAsync)
            .RequireHealingPermission(HealingPermissions.ReportIncident);

        var telemetry = group.MapGroup("/opentelemetry");
        telemetry.MapGet("/collector-configuration", GetCollectorConfigurationAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        telemetry.MapPost("/logs/search", SearchLogsAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        telemetry.MapPost("/traces/search", SearchTracesAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        telemetry.MapGet("/traces/{traceId}", GetTraceAsync)
            .RequireHealingPermission(HealingPermissions.Read);
    }

    private static async ValueTask<object?> RequireApplicationEnvironmentAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(context, WorkspaceOperation.Read);
        if (denied is not null)
            return denied;
        if (!TryGetScope(context, out var scope))
            return NotFound(context);

        var cockpit = context.RequestServices.GetRequiredService<DeploymentCockpitService>();
        var application = (await cockpit.GetCockpitAsync(scope.WorkspaceId, context.RequestAborted)).Applications
            .SingleOrDefault(candidate => Guid.TryParse(candidate.Id, out var id) && id == scope.ApplicationId);
        var hasEnvironment = application?.Environments.Any(candidate =>
            Guid.TryParse(candidate.Id, out var id) && id == scope.EnvironmentId) == true;
        return hasEnvironment ? await next(invocation) : NotFound(context);
    }

    private static async Task<IResult> AppendExplicitIncidentAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        ExplicitHealingIncidentRequest request,
        HttpContext context,
        HealingStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxEnvelopeBytes)
            return Problem(context, StatusCodes.Status413PayloadTooLarge, "healing.intake.too-large", "The incident evidence exceeds the supported size.");

        var validation = Validate(request);
        if (validation is not null)
            return Problem(context, validation.Value.Status, validation.Value.Code, validation.Value.Detail);

        var signal = request.ToSignal(applicationId, environmentId);
        var envelope = JsonSerializer.Serialize(signal);
        if (Encoding.UTF8.GetByteCount(envelope) > MaxEnvelopeBytes)
            return Problem(context, StatusCodes.Status413PayloadTooLarge, "healing.intake.too-large", "The incident evidence exceeds the supported size.");

        var envelopeHash = Sha256(envelope);
        var requestedKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (requestedKey?.Length > MaxIdempotencyKeyLength)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.intake.idempotency-key", "Idempotency-Key is too long.");
        var idempotencyKey = !string.IsNullOrEmpty(requestedKey)
            ? requestedKey
            : !string.IsNullOrWhiteSpace(signal.OccurrenceId)
                ? signal.OccurrenceId.Trim()
                : $"explicit:v1:{envelopeHash}";
        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.intake.occurrence-id", "The occurrence identifier is too long.");

        var item = new HealingSignalInboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            EnvironmentId = environmentId,
            IdempotencyKey = idempotencyKey,
            Source = HealingSignalSource.ExplicitIncident,
            ProfileVersion = signal.ProfileVersion,
            OccurredAt = signal.OccurredAt,
            AcceptedAt = timeProvider.GetUtcNow(),
            RedactedEnvelopeJson = envelope,
            EnvelopeHash = envelopeHash,
            Status = HealingInboxStatus.Pending
        };

        try
        {
            var result = await store.AppendInboxAsync(item, cancellationToken);
            return Results.Accepted(
                uri: null,
                value: new ExplicitHealingIncidentAcceptedResponse(result.Value.Id, result.IsReplay));
        }
        catch (HealingIdempotencyConflictException)
        {
            return Problem(
                context,
                StatusCodes.Status409Conflict,
                "healing.intake.idempotency-conflict",
                "The idempotency key was already used with different incident evidence.");
        }
    }

    private static async Task<IResult> GetCollectorConfigurationAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var query = ResolveTelemetryQuery(context);
        return query is null
            ? TelemetryUnavailable(context)
            : Results.Ok(await query.GetCollectorConfigurationAsync(
                new HealingTelemetryQueryScope(workspaceId, applicationId, environmentId), cancellationToken));
    }

    private static async Task<IResult> SearchLogsAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        OpenTelemetryLogFilter filter,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var query = ResolveTelemetryQuery(context);
        return query is null
            ? TelemetryUnavailable(context)
            : Results.Ok(await query.GetLogsAsync(
                new HealingTelemetryQueryScope(workspaceId, applicationId, environmentId),
                filter with { Take = NormalizeTake(filter.Take) },
                cancellationToken));
    }

    private static async Task<IResult> SearchTracesAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        OpenTelemetryTraceFilter filter,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var query = ResolveTelemetryQuery(context);
        return query is null
            ? TelemetryUnavailable(context)
            : Results.Ok(await query.GetTracesAsync(
                new HealingTelemetryQueryScope(workspaceId, applicationId, environmentId),
                filter with { Take = NormalizeTake(filter.Take) },
                cancellationToken));
    }

    private static async Task<IResult> GetTraceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string traceId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(traceId) || traceId.Length > 128)
            return Problem(context, StatusCodes.Status400BadRequest, "healing.telemetry.trace-id", "Trace ID is invalid.");
        var query = ResolveTelemetryQuery(context);
        if (query is null)
            return TelemetryUnavailable(context);
        var result = await query.GetTraceAsync(
            new HealingTelemetryQueryScope(workspaceId, applicationId, environmentId), traceId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static IWorkspaceHealingOpenTelemetryQuery? ResolveTelemetryQuery(HttpContext context) =>
        context.RequestServices.GetService<IWorkspaceHealingOpenTelemetryQuery>();

    private static int NormalizeTake(int? take) => Math.Clamp(take ?? 100, 1, MaxQueryTake);

    private static (int Status, string Code, string Detail)? Validate(ExplicitHealingIncidentRequest request)
    {
        if (!HealingContractVersion.IsCompatible(HealingContractVersions.SignalProfile, request.ProfileVersion))
            return (StatusCodes.Status400BadRequest, "healing.intake.profile-version", "The Healing signal profile version is unsupported.");
        if (request.OccurredAt == default ||
            string.IsNullOrWhiteSpace(request.OperationName) || request.OperationName.Length > MaxOperationLength ||
            string.IsNullOrWhiteSpace(request.FailureClass) ||
            string.IsNullOrWhiteSpace(request.RetryState) ||
            string.IsNullOrWhiteSpace(request.ServiceName) ||
            request.Exception is null ||
            string.IsNullOrWhiteSpace(request.Exception.Type) || request.Exception.Type.Length > MaxExceptionTypeLength ||
            request.Exception.Message?.Length > MaxMessageLength ||
            request.Exception.StackTrace?.Length > MaxStackLength ||
            request.Exception.Frames is null || request.Exception.Frames.Count > MaxFrames ||
            request.Evidence is null)
        {
            return (StatusCodes.Status400BadRequest, "healing.intake.invalid", "The incident signal is malformed or exceeds a field limit.");
        }
        if (!request.Evidence.IsRedacted)
            return (StatusCodes.Status400BadRequest, "healing.intake.redaction-required", "Explicit incident evidence must be redacted before submission.");
        return null;
    }

    private static bool TryGetScope(HttpContext context, out HealingTelemetryQueryScope scope)
    {
        var workspaceId = Guid.Empty;
        var applicationId = Guid.Empty;
        var environmentId = Guid.Empty;
        var parsed = Guid.TryParse(context.Request.RouteValues["workspaceId"]?.ToString(), out workspaceId) &&
                     Guid.TryParse(context.Request.RouteValues["applicationId"]?.ToString(), out applicationId) &&
                     Guid.TryParse(context.Request.RouteValues["environmentId"]?.ToString(), out environmentId);
        scope = parsed
            ? new HealingTelemetryQueryScope(workspaceId, applicationId, environmentId)
            : new HealingTelemetryQueryScope(Guid.Empty, Guid.Empty, Guid.Empty);
        return parsed;
    }

    private static IResult NotFound(HttpContext context) => Problem(
        context,
        StatusCodes.Status404NotFound,
        "healing.environment.not-found",
        "The application environment was not found.");

    private static IResult TelemetryUnavailable(HttpContext context) => Problem(
        context,
        StatusCodes.Status503ServiceUnavailable,
        "healing.telemetry.unavailable",
        "The Platform OpenTelemetry capability is not configured.");

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(context, status, code, detail);

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
