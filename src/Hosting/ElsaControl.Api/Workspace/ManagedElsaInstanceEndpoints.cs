using ElsaControl.Api.Authentication;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Customer-facing managed-instance routes. The legacy managed-elsa list is kept
/// for the console during migration; the workspace instances routes are canonical.
/// </summary>
public static class ManagedElsaInstanceEndpoints
{
    public static IEndpointRouteBuilder MapManagedElsaInstanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapLegacyList(endpoints);

        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/instances")
            .WithTags("Managed Elsa Instances");

        group.MapGet("", async (
            Guid workspaceId,
            int? page,
            int? pageSize,
            HttpContext context,
            WorkspacePermissionService permissions,
            IManagedElsaInstanceApiStore instances,
            CancellationToken cancellationToken) =>
        {
            var access = context.GetWorkspaceAccess();
            var canOpen = (await permissions.GetEffectivePermissionsAsync(workspaceId, access.AccountId, cancellationToken))
                .Has(ManagedElsaInstancePermissions.Open);
            var all = await instances.ListInstancesAsync(workspaceId, cancellationToken);
            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 50, 1, 100);
            var offset = (long)(currentPage - 1) * currentPageSize;
            var items = offset >= all.Count
                ? new List<ManagedElsaInstanceResponse>()
                : all.Skip((int)offset).Take(currentPageSize)
                    .Select(x => ToResponse(x, canOpen, workspaceId)).ToList();
            return Results.Ok(new ManagedElsaInstanceListResponse(items, currentPage, currentPageSize, all.Count,
                currentPage * currentPageSize < all.Count));
        }).RequireWorkspaceAccess();

        group.MapPost("", async (
            Guid workspaceId,
            ManagedElsaInstanceCreateRequest request,
            HttpContext context,
            IAccountWorkspaceStore accountStore,
            ElsaInstanceLifecycleService lifecycle,
            IElsaInstanceLifecycleStore lifecycleStore,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            var key = RequireIdempotencyKey(context);
            if (key is null)
                return Problem("instance.idempotency-key-required", "Idempotency-Key is required for instance creation.", StatusCodes.Status400BadRequest);
            if (request.Intent is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
                return Problem("instance.shape-invalid", "Instance name, slug and intent are required.", StatusCodes.Status422UnprocessableEntity);

            var access = context.GetWorkspaceAccess();
            var entitlement = await accountStore.GetLatestOrganizationEntitlementAsync(access.OrganizationId, cancellationToken);
            if (entitlement is { ManagedHostingEnabled: false })
                return Problem("instance.entitlement-required", "Managed hosting is not enabled for this organization.", StatusCodes.Status422UnprocessableEntity);

            try
            {
                var normalizedSlug = ElsaInstanceSlug.Normalize(request.Slug);
                // Let the lifecycle service own idempotent replay. A slug preflight
                // must not reject a retry of the original request after its first
                // commit made the slug visible.
                var existingOperation = await lifecycleStore.FindOperationByKeyAsync(workspaceId, key, cancellationToken);
                if (existingOperation is null && (await queries.ListInstancesAsync(workspaceId, cancellationToken)).Any(x =>
                        string.Equals(x.Slug, normalizedSlug, StringComparison.Ordinal)))
                    return Problem("instance.slug-conflict", "The instance slug is already in use in this workspace.", StatusCodes.Status409Conflict);

                var accepted = await lifecycle.CreateAsync(new ElsaInstanceCreateRequest(
                    access.OrganizationId, workspaceId, request.Name, normalizedSlug, request.Intent, key, request.InstanceId), cancellationToken);
                return Accepted(workspaceId, accepted);
            }
            catch (ElsaInstanceLifecycleConflictException exception)
            {
                var statusCode = exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status412PreconditionFailed
                    : StatusCodes.Status409Conflict;
                return Problem(ConflictCode(exception), exception.Message, statusCode);
            }
            catch (ArgumentException exception)
            {
                return Problem("instance.shape-invalid", exception.Message, StatusCodes.Status422UnprocessableEntity);
            }
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapGet("/{instanceId:guid}", async (
            Guid workspaceId,
            Guid instanceId,
            HttpContext context,
            WorkspacePermissionService permissions,
            IElsaInstanceLifecycleStore lifecycle,
            CancellationToken cancellationToken) =>
        {
            var instance = await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken);
            if (instance is null)
                return Results.NotFound();
            var canOpen = (await permissions.GetEffectivePermissionsAsync(workspaceId, context.GetWorkspaceAccess().AccountId, cancellationToken))
                .Has(ManagedElsaInstancePermissions.Open);
            var response = ToResponse(instance, canOpen, workspaceId);
            context.Response.Headers.ETag = response.ETag;
            return Results.Ok(response);
        }).RequireWorkspaceAccess();

        group.MapMethods("/{instanceId:guid}", [HttpMethods.Patch], async (
            Guid workspaceId,
            Guid instanceId,
            ManagedElsaInstancePatchRequest request,
            HttpContext context,
            IElsaInstanceLifecycleStore store,
            ElsaInstanceLifecycleService lifecycle,
            CancellationToken cancellationToken) =>
        {
            var precondition = ReadIfMatch(context.Request);
            if (precondition is null)
                return Problem("instance.if-match-required", "If-Match is required for instance updates.", StatusCodes.Status428PreconditionRequired);
            var key = RequireIdempotencyKey(context);
            if (key is null)
                return Problem("instance.idempotency-key-required", "Idempotency-Key is required for instance updates.", StatusCodes.Status400BadRequest);
            var existing = await store.GetInstanceAsync(workspaceId, instanceId, cancellationToken);
            if (existing is null)
                return Results.NotFound();
            if (request.Intent is null && request.Name is null)
                return Problem("instance.shape-invalid", "At least one mutable instance field is required.", StatusCodes.Status422UnprocessableEntity);
            try
            {
                var accepted = await lifecycle.UpdateIntentAsync(new ElsaInstanceIntentUpdateRequest(
                    workspaceId, instanceId, request.Intent ?? existing.Intent, precondition.Value, key, request.Name, request.Reason), cancellationToken);
                return Accepted(workspaceId, accepted);
            }
            catch (ElsaInstanceLifecycleConflictException exception)
            {
                return Problem(ConflictCode(exception), exception.Message,
                    exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status412PreconditionFailed : StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase))
            {
                return Problem("instance.version-conflict", exception.Message, StatusCodes.Status412PreconditionFailed);
            }
            catch (ArgumentException exception)
            {
                return Problem("instance.shape-invalid", exception.Message, StatusCodes.Status422UnprocessableEntity);
            }
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapPost("/{instanceId:guid}/operations", async (
            Guid workspaceId,
            Guid instanceId,
            ManagedElsaInstanceOperationRequest request,
            HttpContext context,
            ElsaInstanceLifecycleService lifecycle,
            CancellationToken cancellationToken) =>
        {
            var key = RequireIdempotencyKey(context);
            if (key is null)
                return Problem("instance.idempotency-key-required", "Idempotency-Key is required for instance operations.", StatusCodes.Status400BadRequest);
            if (!Enum.IsDefined(request.Action) || request.Action is ElsaInstanceOperationAction.Create or ElsaInstanceOperationAction.UpdateIntent)
                return Problem("instance.operation-invalid", "The requested operation is not supported on this route.", StatusCodes.Status422UnprocessableEntity);
            var ifMatch = ReadIfMatch(context.Request);
            var expectedVersion = ifMatch ?? request.ExpectedVersion;
            if (expectedVersion is null)
                return Problem("instance.if-match-required", "If-Match or expectedVersion is required for instance operations.", StatusCodes.Status428PreconditionRequired);
            if (ifMatch is { } ifMatchValue && request.ExpectedVersion is { } bodyVersion && ifMatchValue != bodyVersion)
                return Problem("instance.version-conflict", "If-Match and expectedVersion do not agree.", StatusCodes.Status412PreconditionFailed);

            try
            {
                var operationRequest = new ElsaInstanceLifecycleRequest(workspaceId, instanceId, expectedVersion.Value, key, request.Reason);
                ElsaInstanceLifecycleAcceptance accepted;
                if (request.Action is ElsaInstanceOperationAction.ApproveMinorUpgrade or ElsaInstanceOperationAction.MajorMigration)
                {
                    if (request.Intent is null)
                        return Problem("instance.intent-required", "This operation requires a new intent.", StatusCodes.Status422UnprocessableEntity);
                    var intentRequest = new ElsaInstanceIntentUpdateRequest(workspaceId, instanceId, request.Intent,
                        expectedVersion.Value, key, request.Name, request.Reason);
                    accepted = request.Action == ElsaInstanceOperationAction.ApproveMinorUpgrade
                        ? await lifecycle.ApproveMinorUpgradeAsync(intentRequest, cancellationToken)
                        : await lifecycle.MajorMigrationAsync(intentRequest, cancellationToken);
                }
                else
                {
                    accepted = request.Action switch
                    {
                        ElsaInstanceOperationAction.Start => await lifecycle.StartAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Stop => await lifecycle.StopAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Restart => await lifecycle.RestartAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Reconcile => await lifecycle.ReconcileAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Recover => await lifecycle.RecoverAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Retry => await lifecycle.RetryAsync(operationRequest, cancellationToken),
                        ElsaInstanceOperationAction.Delete => await lifecycle.DeleteAsync(operationRequest, cancellationToken),
                        _ => throw new ArgumentOutOfRangeException(nameof(request.Action))
                    };
                }
                return Accepted(workspaceId, accepted);
            }
            catch (ElsaInstanceLifecycleConflictException exception)
            {
                return Problem(ConflictCode(exception), exception.Message, StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase))
            {
                return Problem("instance.version-conflict", exception.Message, StatusCodes.Status412PreconditionFailed);
            }
            catch (InvalidOperationException exception)
            {
                var code = exception.Message.Contains("already active", StringComparison.OrdinalIgnoreCase)
                    ? "instance.operation-active"
                    : "instance.operation-conflict";
                return Problem(code, exception.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException exception)
            {
                return Problem("instance.operation-invalid", exception.Message, StatusCodes.Status422UnprocessableEntity);
            }
        }).RequireWorkspaceAccess(WorkspaceOperation.MutateWorkspaceResource);

        group.MapGet("/{instanceId:guid}/operations/{operationId:guid}", async (
            Guid workspaceId,
            Guid instanceId,
            Guid operationId,
            HttpContext context,
            IElsaInstanceLifecycleStore lifecycle,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            var operation = await queries.GetOperationAsync(workspaceId, instanceId, operationId, cancellationToken);
            if (operation is null)
                return Results.NotFound();
            var instance = await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken);
            if (instance is null)
                return Results.NotFound();
            context.Response.Headers.ETag = ETag(instance.Version);
            return Results.Ok(ToOperationResponse(workspaceId, instanceId, operation));
        }).RequireWorkspaceAccess();

        group.MapGet("/{instanceId:guid}/revisions", async (
            Guid workspaceId, Guid instanceId,
            IElsaInstanceLifecycleStore lifecycle,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            if (await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken) is null)
                return Results.NotFound();
            return Results.Ok(new ManagedElsaInstanceRevisionsResponse(await queries.ListRevisionsAsync(workspaceId, instanceId, cancellationToken)));
        }).RequireWorkspaceAccess();

        group.MapGet("/{instanceId:guid}/resolved-plans/{planId}", async (
            Guid workspaceId, Guid instanceId, string planId,
            IElsaInstanceLifecycleStore lifecycle,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            if (await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken) is null)
                return Results.NotFound();
            var plan = await queries.GetResolvedPlanAsync(workspaceId, instanceId, planId, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        }).RequireWorkspaceAccess();

        group.MapGet("/{instanceId:guid}/deployments", async (
            Guid workspaceId, Guid instanceId,
            IElsaInstanceLifecycleStore lifecycle,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            if (await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken) is null)
                return Results.NotFound();
            return Results.Ok(new ManagedElsaInstanceDeploymentsResponse(await queries.ListDeploymentsAsync(workspaceId, instanceId, cancellationToken)));
        }).RequireWorkspaceAccess();

        group.MapGet("/{instanceId:guid}/audit", async (
            Guid workspaceId, Guid instanceId,
            IElsaInstanceLifecycleStore lifecycle,
            IManagedElsaInstanceApiStore queries,
            CancellationToken cancellationToken) =>
        {
            if (await lifecycle.GetInstanceAsync(workspaceId, instanceId, cancellationToken) is null)
                return Results.NotFound();
            return Results.Ok(new ManagedElsaInstanceAuditResponse(await queries.ListAuditAsync(workspaceId, instanceId, cancellationToken)));
        }).RequireWorkspaceAccess();

        return endpoints;
    }

    private static void MapLegacyList(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/managed-elsa/instances", async (
            Guid workspaceId, HttpContext context, WorkspacePermissionService permissions,
            IManagedElsaInstanceCatalog instances, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            var canOpen = (await permissions.GetEffectivePermissionsAsync(workspaceId, context.GetWorkspaceAccess().AccountId, cancellationToken))
                .Has(ManagedElsaInstancePermissions.Open);
            var summaries = await instances.ListAsync(workspaceId, cancellationToken);
            return Results.Ok(summaries.Select(summary => ToLegacyResponse(summary, canOpen)).ToList());
        }).WithTags("Managed Elsa Instances").RequireWorkspaceAccess();
    }

    private static IResult Accepted(Guid workspaceId, ElsaInstanceLifecycleAcceptance accepted)
    {
        var location = $"/api/workspaces/{workspaceId:D}/instances/{accepted.Instance.Id:D}/operations/{accepted.Operation.Id:D}";
        var operation = new ElsaInstanceOperationSummary(accepted.Operation.Id, accepted.Operation.InstanceId,
            accepted.Operation.Action, accepted.Operation.State, accepted.Operation.ExpectedVersion,
            accepted.Operation.AttemptNumber, accepted.Operation.AcceptedAt, null, null,
            accepted.Instance.DesiredStateRevisionId?.Value, accepted.Instance.ResolvedPlanReference?.PlanId,
            null, null, null, null);
        return Results.Accepted(location, new ManagedElsaInstanceAcceptedResponse(
            // Mutations may be accepted after an open grant is revoked. The
            // operation response remains useful while the instance projection is
            // always redacted unless the caller explicitly reads it with access.
            ToResponse(accepted.Instance, false, workspaceId),
            ToOperationResponse(workspaceId, accepted.Instance.Id, operation),
            new Dictionary<string, string> { ["self"] = location }));
    }

    private static ManagedElsaInstanceResponse ToResponse(ElsaInstance instance, bool canOpen, Guid workspaceId)
    {
        var binding = canOpen ? instance.IdentityBinding : null;
        return new ManagedElsaInstanceResponse(instance.OrganizationId, instance.Id, instance.Name, instance.Slug,
            instance.DesiredLifecycle, instance.ObservedLifecycle, instance.Health, canOpen && binding is not null,
            canOpen && binding is not null ? binding.Audience : null,
            canOpen && binding is not null ? binding.CanonicalCallbackUri : null,
            !canOpen ? "Not authorized to open this instance." : binding is null ? "The current identity binding is unavailable." : null)
        {
            Version = instance.Version,
            ETag = ETag(instance.Version),
            DesiredStateRevisionId = instance.DesiredStateRevisionId?.Value,
            ResolvedPlan = instance.ResolvedPlanReference,
            CurrentResolvedRelease = instance.CurrentResolvedRelease,
            CurrentDeployment = instance.CurrentDeploymentReference,
            IdentityBinding = binding is null ? null : new ManagedElsaInstanceIdentityBindingResponse(
                binding.Audience, binding.CanonicalCallbackUri, binding.VerifiedEndpointOrigin, binding.BindingVersion, binding.ChangedAt),
            IdentityBindingState = !canOpen ? "not-authorized" : binding is null ? "identity-unavailable" : "available",
            Intent = instance.Intent,
            Links = new Dictionary<string, string>
            {
                ["self"] = $"/api/workspaces/{workspaceId:D}/instances/{instance.Id:D}",
                ["operations"] = $"/api/workspaces/{workspaceId:D}/instances/{instance.Id:D}/operations",
                ["revisions"] = $"/api/workspaces/{workspaceId:D}/instances/{instance.Id:D}/revisions",
                ["deployments"] = $"/api/workspaces/{workspaceId:D}/instances/{instance.Id:D}/deployments",
                ["audit"] = $"/api/workspaces/{workspaceId:D}/instances/{instance.Id:D}/audit"
            }
        };
    }

    private static ManagedElsaInstanceOperationResponse ToOperationResponse(Guid workspaceId, Guid instanceId, ElsaInstanceOperationSummary operation) =>
        new(operation.Id, instanceId, operation.Action, operation.State, operation.ExpectedVersion, operation.AttemptNumber,
            operation.AcceptedAt, operation.StartedAt, operation.CompletedAt, operation.DesiredStateRevisionId,
            operation.ResolvedPlanId, operation.DeploymentRunId, operation.FailureCode, operation.ReconciledObservedLifecycle,
            operation.ReconciledHealth, new Dictionary<string, string>
            {
                ["self"] = $"/api/workspaces/{workspaceId:D}/instances/{instanceId:D}/operations/{operation.Id:D}",
                ["instance"] = $"/api/workspaces/{workspaceId:D}/instances/{instanceId:D}"
            });

    private static ManagedElsaInstanceResponse ToLegacyResponse(ManagedElsaInstanceSummary summary, bool canOpen)
    {
        var healthy = summary.DesiredLifecycle == ElsaDesiredLifecycle.Running && summary.ObservedLifecycle == ElsaObservedLifecycle.Ready && summary.Health == ElsaInstanceHealth.Healthy;
        var openable = canOpen && healthy && summary.Audience is not null && summary.CallbackUri is not null;
        return new ManagedElsaInstanceResponse(summary.OrganizationId, summary.InstanceId, summary.Name, summary.Slug,
            summary.DesiredLifecycle, summary.ObservedLifecycle, summary.Health, openable,
            openable ? summary.Audience : null, openable ? summary.CallbackUri!.OriginalString : null,
            !canOpen ? "Not authorized to open this instance." : !healthy ? "This instance is not currently available." : !openable ? "The current instance binding is unavailable." : null);
    }

    private static string? RequireIdempotencyKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() is { Length: > 0 } key ? key : null;

    private static int? ReadIfMatch(HttpRequest request)
    {
        var value = request.Headers.IfMatch.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value == "*" || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"')
            return null;
        value = value[1..^1];
        return int.TryParse(value, out var version) && version > 0 ? version : null;
    }

    private static string ETag(int version) => $"\"{version}\"";

    private static string ConflictCode(ElsaInstanceLifecycleConflictException exception) =>
        exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase) ? "instance.version-conflict" :
        exception.Message.Contains("operation", StringComparison.OrdinalIgnoreCase) ? "instance.operation-active" :
        "instance.idempotency-conflict";

    private static IResult Problem(string code, string title, int statusCode) => Results.Problem(title: title, statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record ManagedElsaInstanceCreateRequest(string? Name, string? Slug, ElsaInstanceIntent? Intent, Guid? InstanceId = null);
public sealed record ManagedElsaInstancePatchRequest(ElsaInstanceIntent? Intent = null, string? Name = null, string? Reason = null);
public sealed record ManagedElsaInstanceOperationRequest(ElsaInstanceOperationAction Action, int? ExpectedVersion = null, string? Reason = null, ElsaInstanceIntent? Intent = null, string? Name = null);
public sealed record ManagedElsaInstanceListResponse(IReadOnlyList<ManagedElsaInstanceResponse> Items, int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record ManagedElsaInstanceAcceptedResponse(ManagedElsaInstanceResponse Instance, ManagedElsaInstanceOperationResponse Operation, IReadOnlyDictionary<string, string> Links);
public sealed record ManagedElsaInstanceOperationResponse(Guid Id, Guid InstanceId, ElsaInstanceOperationAction Action, ElsaInstanceOperationState State, int ExpectedVersion, int AttemptNumber, DateTimeOffset AcceptedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? DesiredStateRevisionId, string? ResolvedPlanId, Guid? DeploymentRunId, string? FailureCode, ElsaObservedLifecycle? ReconciledObservedLifecycle, ElsaInstanceHealth? ReconciledHealth, IReadOnlyDictionary<string, string> Links);
public sealed record ManagedElsaInstanceIdentityBindingResponse(string Audience, string CanonicalCallbackUri, string VerifiedEndpointOrigin, int BindingVersion, DateTimeOffset ChangedAt);
public sealed record ManagedElsaInstanceRevisionsResponse(IReadOnlyList<ElsaInstanceIntentRevisionSummary> Items);
public sealed record ManagedElsaInstanceDeploymentsResponse(IReadOnlyList<ElsaInstanceDeploymentSummary> Items);
public sealed record ManagedElsaInstanceAuditResponse(IReadOnlyList<ElsaInstanceAuditEventSummary> Items);

public sealed record ManagedElsaInstanceResponse(Guid OrganizationId, Guid InstanceId, string Name, string Slug, ElsaDesiredLifecycle DesiredLifecycle, ElsaObservedLifecycle ObservedLifecycle, ElsaInstanceHealth Health, bool CanOpen, string? Audience, string? RedirectUri, string? UnavailableReason)
{
    public int Version { get; init; }
    public string ETag { get; init; } = "";
    public string? DesiredStateRevisionId { get; init; }
    public ElsaResolvedPlanReference? ResolvedPlan { get; init; }
    public ElsaCurrentResolvedRelease? CurrentResolvedRelease { get; init; }
    public ElsaCurrentDeploymentReference? CurrentDeployment { get; init; }
    public ManagedElsaInstanceIdentityBindingResponse? IdentityBinding { get; init; }
    public string IdentityBindingState { get; init; } = "identity-unavailable";
    public ElsaInstanceIntent? Intent { get; init; }
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();
}
