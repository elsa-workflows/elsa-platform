using System.Security.Cryptography;
using System.Text;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Manifests;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using ContractManifest = ValenceControl.Healing.ComponentManifest.HealingComponentManifest;
using ContractManifestSerializer = ValenceControl.Healing.ComponentManifest.ComponentManifestSerializer;
using ContractManifestValidationException = ValenceControl.Healing.ComponentManifest.ComponentManifestValidationException;
using CoreManifest = ValenceControl.Healing.Core.ComponentManifest;

namespace ValenceControl.Api.Workspace.Healing;

public sealed class WorkspaceHealingConfigurationEndpointModule : IHealingEndpointModule
{
    private const int MaxManifestBodyBytes = 262_144;
    private const int MaxKeyLength = 256;
    private const int MaxNameLength = 512;
    private const int MaxPathLength = 2_048;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing/applications/{applicationId:guid}");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(context, WorkspaceOperation.Read);
            if (denied is not null)
                return denied;
            if (!Guid.TryParse(context.Request.RouteValues["workspaceId"]?.ToString(), out var workspaceId) ||
                !Guid.TryParse(context.Request.RouteValues["applicationId"]?.ToString(), out var applicationId))
                return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
            var cockpit = context.RequestServices.GetRequiredService<DeploymentCockpitService>();
            return await FindApplicationAsync(cockpit, workspaceId, applicationId, context.RequestAborted) is null
                ? HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.")
                : await next(invocation);
        });

        group.MapGet("/configuration", GetConfigurationAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPut("/configuration", PutConfigurationAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/confirmations", CreateConfirmationAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/stop", StopAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/resume", ResumeAsync)
            .RequireHealingPermission(HealingPermissions.Configure);

        group.MapPost("/revisions/{revisionId:guid}/component-manifests", RegisterManifestAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapGet("/component-manifests", ListManifestsAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapGet("/component-manifests/{manifestId:guid}", GetManifestAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/component-manifests/{manifestId:guid}/verify", VerifyManifestAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/component-manifests/{manifestId:guid}/revoke", RevokeManifestAsync)
            .RequireHealingPermission(HealingPermissions.Configure);

        group.MapGet("/source-ownership-bindings", ListBindingsAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/source-ownership-bindings", CreateBindingAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPut("/source-ownership-bindings/{bindingId:guid}", UpdateBindingAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/source-ownership-bindings/{bindingId:guid}/activate", ActivateBindingAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/source-ownership-bindings/{bindingId:guid}/suspend", SuspendBindingAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/source-ownership-bindings/{bindingId:guid}/revoke", RevokeBindingAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
    }

    private static async Task<IResult> GetConfigurationAsync(
        Guid workspaceId,
        Guid applicationId,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingConfigurationService service,
        CancellationToken cancellationToken)
    {
        var application = await FindApplicationAsync(cockpitService, workspaceId, applicationId, cancellationToken);
        if (application is null)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");

        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var result = await service.GetAsync(workspaceId, applicationId, authorization, cancellationToken);
        var configuration = result.Succeeded ? result.Value! : CreateDefaultConfiguration(workspaceId, applicationId, application);
        return Results.Ok(await ConfigurationResponseAsync(context, application, configuration, cancellationToken));
    }

    private static async Task<IResult> PutConfigurationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateHealingConfigurationRequest request,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingConfigurationService service,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        var application = await FindApplicationAsync(cockpitService, workspaceId, applicationId, cancellationToken);
        if (application is null)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");

        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var existing = await service.GetAsync(workspaceId, applicationId, authorization, cancellationToken);
        if (existing.Succeeded && !string.Equals(request.Version, Convert.ToBase64String(existing.Value!.Version), StringComparison.Ordinal))
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.configuration.stale", "The Healing configuration changed after it was loaded.");
        var requestedEnvironmentIds = request.Environments.Select(x => x.EnvironmentId).ToArray();
        if (requestedEnvironmentIds.Distinct().Count() != requestedEnvironmentIds.Length)
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.environment.duplicate", "Each application environment can be configured only once.");
        var applicationEnvironmentIds = application.Environments
            .Select(x => Guid.TryParse(x.Id, out var environmentId) ? environmentId : (Guid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        if (requestedEnvironmentIds.Any(x => !applicationEnvironmentIds.Contains(x)))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.environment.not-found", "A configured environment does not belong to the application.");
        var automaticMergeChanged = existing.Succeeded
            ? existing.Value!.AutomaticMergeEnabled != request.AutomaticMergeEnabled
            : request.AutomaticMergeEnabled;
        if (automaticMergeChanged)
        {
            if (request.ConfirmationId is null)
                return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.confirmation.required", "A target-bound confirmation is required.");
            var access = context.GetWorkspaceAccess();
            var consumed = await confirmations.ConsumeConfirmationAsync(
                workspaceId,
                request.ConfirmationId.Value,
                access.AccountId,
                ConfirmationActionType.HealingAutomaticMerge,
                AutomaticMergeTarget(applicationId, request.AutomaticMergeEnabled),
                cancellationToken);
            if (!consumed.Succeeded)
                return HealingProblem(context, StatusCodes.Status400BadRequest, consumed.Validation.Id, consumed.Validation.Message);
        }

        var configuration = ToConfiguration(workspaceId, applicationId, request, existing.Value);
        HealingOperationResult<HealingConfiguration> saved;
        try
        {
            saved = await service.SaveAsync(configuration, authorization, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.configuration.stale", "The Healing configuration changed after it was loaded.");
        }
        if (!saved.Succeeded)
            return OperationFailure(context, saved.ReasonCode);
        return Results.Ok(await ConfigurationResponseAsync(context, application, saved.Value!, cancellationToken));
    }

    private static async Task<IResult> CreateConfirmationAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingConfirmationRequest request,
        HttpContext context,
        WorkspacePermissionService permissions,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        var access = context.GetWorkspaceAccess();
        if (request.ActionType == ConfirmationActionType.HealingAutomaticMerge)
        {
            var effective = await permissions.GetEffectiveHealingPermissionsAsync(access, cancellationToken);
            if (!effective.Has(HealingPermissions.ConfigureAutoMerge) || request.AutomaticMergeEnabled is null)
                return HealingPermissionEndpointFilters.HealingPermissionDenied(context, HealingPermissions.ConfigureAutoMerge);
        }
        else if (request.ActionType is not (ConfirmationActionType.HealingEmergencyStop or ConfirmationActionType.HealingEmergencyResume))
        {
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.confirmation.action", "Unsupported Healing confirmation action.");
        }

        var target = request.ActionType switch
        {
            ConfirmationActionType.HealingEmergencyStop => EmergencyStopTarget(applicationId),
            ConfirmationActionType.HealingEmergencyResume => EmergencyResumeTarget(applicationId),
            _ => AutomaticMergeTarget(applicationId, request.AutomaticMergeEnabled!.Value)
        };
        var confirmation = await confirmations.CreateConfirmationAsync(
            workspaceId,
            new CreateActionConfirmationRequest(request.ActionType, target, access.AccountId, TimeSpan.FromMinutes(5)),
            cancellationToken);
        return Results.Created($"/api/workspaces/{workspaceId:D}/healing/applications/{applicationId:D}/confirmations/{confirmation.Id:D}", confirmation);
    }

    private static async Task<IResult> StopAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingStopRequest request,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingConfigurationService service,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        var application = await FindApplicationAsync(cockpitService, workspaceId, applicationId, cancellationToken);
        if (application is null)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        var access = context.GetWorkspaceAccess();
        var consumed = await confirmations.ConsumeConfirmationAsync(
            workspaceId, request.ConfirmationId, access.AccountId,
            ConfirmationActionType.HealingEmergencyStop, EmergencyStopTarget(applicationId), cancellationToken);
        if (!consumed.Succeeded)
            return HealingProblem(context, StatusCodes.Status400BadRequest, consumed.Validation.Id, consumed.Validation.Message);

        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        HealingOperationResult<HealingConfiguration> result;
        try
        {
            result = await service.EmergencyStopAsync(workspaceId, applicationId, authorization, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.configuration.stale", "The Healing configuration changed while the emergency stop was being applied.");
        }
        if (!result.Succeeded)
            return OperationFailure(context, result.ReasonCode);
        return Results.Ok(await ConfigurationResponseAsync(context, application, result.Value!, cancellationToken));
    }

    private static async Task<IResult> ResumeAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingStopRequest request,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        HealingConfigurationService service,
        ConfirmationService confirmations,
        CancellationToken cancellationToken)
    {
        var application = await FindApplicationAsync(cockpitService, workspaceId, applicationId, cancellationToken);
        if (application is null)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        var access = context.GetWorkspaceAccess();
        var consumed = await confirmations.ConsumeConfirmationAsync(
            workspaceId, request.ConfirmationId, access.AccountId,
            ConfirmationActionType.HealingEmergencyResume, EmergencyResumeTarget(applicationId), cancellationToken);
        if (!consumed.Succeeded)
            return HealingProblem(context, StatusCodes.Status400BadRequest, consumed.Validation.Id, consumed.Validation.Message);

        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        HealingOperationResult<HealingConfiguration> result;
        try
        {
            result = await service.ResumeAsync(workspaceId, applicationId, authorization, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.configuration.stale", "The Healing configuration changed while Healing was being resumed.");
        }
        if (!result.Succeeded)
            return OperationFailure(context, result.ReasonCode);
        return Results.Ok(await ConfigurationResponseAsync(context, application, result.Value!, cancellationToken));
    }

    private static async Task<IResult> RegisterManifestAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid revisionId,
        HttpContext context,
        DeploymentCockpitService cockpitService,
        IWorkspaceDeploymentStore deploymentStore,
        ComponentManifestService service,
        CancellationToken cancellationToken)
    {
        if (await FindApplicationAsync(cockpitService, workspaceId, applicationId, cancellationToken) is null)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        var revision = await deploymentStore.GetRevisionAsync(workspaceId, revisionId, cancellationToken);
        if (revision?.ApplicationId != applicationId)
            return HealingProblem(context, StatusCodes.Status404NotFound, "healing.revision.not-found", "Revision was not found.");
        if (!context.Request.Headers.ContainsKey("Idempotency-Key") ||
            context.Request.Headers["Idempotency-Key"].Count == 1 &&
            string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"][0]))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.idempotency-key.required", "Idempotency-Key is required.");
        if (!TryGetSingleHeader(context, "Idempotency-Key", out var idempotencyKey) ||
            idempotencyKey.Length > MaxKeyLength ||
            !string.Equals(idempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal) ||
            idempotencyKey.Any(char.IsControl))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.idempotency-key.invalid", "Idempotency-Key is invalid.");
        if (!context.Request.Headers.ContainsKey("Content-Digest") ||
            context.Request.Headers["Content-Digest"].Count == 1 &&
            string.IsNullOrWhiteSpace(context.Request.Headers["Content-Digest"][0]))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.content-digest.required", "Content-Digest is required.");
        if (!TryGetSingleHeader(context, "Content-Digest", out var contentDigest) || !IsSha256Digest(contentDigest))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.content-digest.invalid", "Content-Digest must be a lowercase SHA-256 digest.");

        var bodyRead = await ReadManifestBodyAsync(context.Request, cancellationToken);
        if (bodyRead.TooLarge)
            return HealingProblem(context, StatusCodes.Status413PayloadTooLarge, "healing.manifest.too-large", $"The component manifest must not exceed {MaxManifestBodyBytes} bytes.");
        if (bodyRead.Body is null)
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.manifest.encoding", "The component manifest must use valid UTF-8 encoding.");
        var body = bodyRead.Body;
        var actualContentDigest = Sha256(body);
        if (!string.Equals(contentDigest, actualContentDigest, StringComparison.Ordinal))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.content-digest.mismatch", "Content-Digest does not match the request body.");

        ContractManifest contract;
        try
        {
            contract = ContractManifestSerializer.Deserialize(body);
        }
        catch (Exception exception) when (exception is ContractManifestValidationException or System.Text.Json.JsonException)
        {
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.manifest.invalid", "The component manifest is invalid.");
        }

        var canonicalJson = ContractManifestSerializer.Serialize(contract);
        if (!string.Equals(body, canonicalJson, StringComparison.Ordinal))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.manifest.non-canonical", "The component manifest body must use canonical serialization.");
        if (string.IsNullOrWhiteSpace(revision.Commit))
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.revision.commit-missing", "The selected deployment revision has no source commit.");
        if (!string.Equals(contract.Revision.SourceRevision, revision.Commit, StringComparison.Ordinal))
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.manifest.revision-mismatch", "The manifest source revision does not match the selected deployment revision commit.");

        var manifest = ToCoreManifest(workspaceId, applicationId, revisionId, contract, canonicalJson);
        var result = await service.RegisterAsync(
            manifest,
            idempotencyKey,
            actualContentDigest,
            await CreateAuthorizationAsync(context, applicationId, cancellationToken),
            cancellationToken);
        if (!result.Succeeded)
            return OperationFailure(context, result.ReasonCode);
        return result.IsReplay
            ? Results.Ok(ToManifestResponse(result.Manifest!, []))
            : Results.Created($"/api/workspaces/{workspaceId:D}/healing/applications/{applicationId:D}/component-manifests/{result.Manifest!.Id:D}", ToManifestResponse(result.Manifest, []));
    }

    private static async Task<IResult> ListManifestsAsync(Guid workspaceId, Guid applicationId, HttpContext context, ComponentManifestService service, SourceOwnershipService ownership, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var manifests = await service.ListAsync(workspaceId, applicationId, authorization, cancellationToken);
        var items = new List<object>(manifests.Count);
        foreach (var manifest in manifests)
            items.Add(await ProjectManifestAsync(manifest, ownership, authorization, cancellationToken));
        return Results.Ok(new { items, canApproveOwnership = authorization.IsWorkspaceOwner });
    }

    private static async Task<IResult> GetManifestAsync(Guid workspaceId, Guid applicationId, Guid manifestId, HttpContext context, ComponentManifestService service, SourceOwnershipService ownership, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var result = await service.GetAsync(workspaceId, applicationId, manifestId, authorization, cancellationToken);
        if (!result.Succeeded)
            return OperationFailure(context, result.ReasonCode);
        return Results.Ok(await ProjectManifestAsync(result.Value!, ownership, authorization, cancellationToken));
    }

    private static async Task<IResult> VerifyManifestAsync(Guid workspaceId, Guid applicationId, Guid manifestId, HttpContext context, ComponentManifestService service, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var result = await service.VerifyByOwnerAsync(workspaceId, applicationId, manifestId, authorization, cancellationToken);
        return result.Succeeded ? Results.Ok(ToManifestResponse(result.Value!, [])) : OperationFailure(context, result.ReasonCode);
    }

    private static async Task<IResult> RevokeManifestAsync(Guid workspaceId, Guid applicationId, Guid manifestId, HttpContext context, ComponentManifestService service, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var result = await service.RevokeAsync(workspaceId, applicationId, manifestId, authorization, cancellationToken);
        return result.Succeeded ? Results.Ok(ToManifestResponse(result.Value!, [])) : OperationFailure(context, result.ReasonCode);
    }

    private static async Task<IResult> ListBindingsAsync(Guid workspaceId, Guid applicationId, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var bindings = await service.ListAsync(workspaceId, applicationId, authorization, cancellationToken);
        return Results.Ok(new { items = bindings.Select(ToBindingResponse), permissions = authorization.Permissions.Order(), canApproveOwnership = authorization.IsWorkspaceOwner });
    }

    private static async Task<IResult> CreateBindingAsync(Guid workspaceId, Guid applicationId, SourceOwnershipBindingRequest request, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken)
    {
        var validation = ValidateBindingRequest(context, request, requireVersion: false, out _);
        return validation ?? await SaveBindingAsync(workspaceId, applicationId, Guid.NewGuid(), request, context, service, cancellationToken);
    }

    private static async Task<IResult> UpdateBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, SourceOwnershipBindingRequest request, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken)
    {
        var validation = ValidateBindingRequest(context, request, requireVersion: true, out var expectedVersion);
        if (validation is not null)
            return validation;
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var existing = await service.GetAsync(workspaceId, applicationId, bindingId, authorization, cancellationToken);
        if (!existing.Succeeded)
            return OperationFailure(context, existing.ReasonCode);
        if (!CryptographicOperations.FixedTimeEquals(expectedVersion, existing.Value!.Version))
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.binding.stale", "The source ownership binding changed after it was loaded.");
        var binding = ToBinding(workspaceId, applicationId, bindingId, request);
        binding.CreatedAt = existing.Value!.CreatedAt;
        binding.Version = expectedVersion;
        binding.Status = existing.Value.Status;
        try
        {
            var result = await service.SaveDraftAsync(binding, authorization, cancellationToken);
            return result.Succeeded ? Results.Ok(ToBindingResponse(result.Value!)) : OperationFailure(context, result.ReasonCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.binding.stale", "The source ownership binding changed after it was loaded.");
        }
    }

    private static Task<IResult> ActivateBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken) =>
        BindingTransitionAsync(context, async authorization =>
        {
            var current = await service.GetAsync(workspaceId, applicationId, bindingId, authorization, cancellationToken);
            return current.Succeeded
                ? await service.ActivateAsync(current.Value!, authorization, cancellationToken)
                : current;
        }, applicationId, cancellationToken);

    private static Task<IResult> SuspendBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken) =>
        BindingTransitionAsync(context, authorization => service.SuspendAsync(workspaceId, applicationId, bindingId, authorization, cancellationToken), applicationId, cancellationToken);

    private static Task<IResult> RevokeBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken) =>
        BindingTransitionAsync(context, authorization => service.RevokeAsync(workspaceId, applicationId, bindingId, authorization, cancellationToken), applicationId, cancellationToken);

    private static async Task<IResult> SaveBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, SourceOwnershipBindingRequest request, HttpContext context, SourceOwnershipService service, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, cancellationToken);
        var binding = ToBinding(workspaceId, applicationId, bindingId, request);
        var result = await service.SaveDraftAsync(binding, authorization, cancellationToken);
        return result.Succeeded ? Results.Ok(ToBindingResponse(result.Value!)) : OperationFailure(context, result.ReasonCode);
    }

    private static async Task<IResult> BindingTransitionAsync(HttpContext context, Func<HealingAuthorization, ValueTask<HealingOperationResult<SourceOwnershipBinding>>> transition, Guid applicationId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await transition(await CreateAuthorizationAsync(context, applicationId, cancellationToken));
            return result.Succeeded ? Results.Ok(ToBindingResponse(result.Value!)) : OperationFailure(context, result.ReasonCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HealingProblem(context, StatusCodes.Status409Conflict, "healing.binding.stale", "The source ownership binding changed after it was loaded.");
        }
    }

    private static async Task<HealingAuthorization> CreateAuthorizationAsync(HttpContext context, Guid applicationId, CancellationToken cancellationToken)
    {
        var access = context.GetWorkspaceAccess();
        var effective = context.Items[HealingPermissionEndpointFilters.EffectivePermissionsItemKey] as EffectiveWorkspacePermissions;
        if (effective is null)
        {
            var service = context.RequestServices.GetRequiredService<WorkspacePermissionService>();
            effective = await service.GetEffectiveHealingPermissionsAsync(access, cancellationToken);
            context.Items[HealingPermissionEndpointFilters.EffectivePermissionsItemKey] = effective;
        }
        return new HealingAuthorization(access.WorkspaceId, applicationId, access.AccountId.ToString("D"), access.Role == WorkspaceRole.Owner, effective.Permissions);
    }

    private static async Task<WorkflowApplication?> FindApplicationAsync(DeploymentCockpitService service, Guid workspaceId, Guid applicationId, CancellationToken cancellationToken) =>
        (await service.GetCockpitAsync(workspaceId, cancellationToken)).Applications.SingleOrDefault(x => string.Equals(x.Id, applicationId.ToString("D"), StringComparison.OrdinalIgnoreCase));

    private static HealingConfiguration CreateDefaultConfiguration(Guid workspaceId, Guid applicationId, WorkflowApplication application) => new()
    {
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        SignalProfileVersion = "1.0",
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromMinutes(15),
        TimeBudget = TimeSpan.FromMinutes(30),
        ConcurrencyBudget = 1,
        Environments = application.Environments.Where(x => Guid.TryParse(x.Id, out _)).Select(x => new HealingEnvironmentConfiguration { EnvironmentId = Guid.Parse(x.Id) }).ToList()
    };

    private static HealingConfiguration ToConfiguration(Guid workspaceId, Guid applicationId, UpdateHealingConfigurationRequest request, HealingConfiguration? existing) => new()
    {
        Id = existing?.Id ?? Guid.Empty,
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        DiscoveryEnabled = request.DiscoveryEnabled,
        RepairEnabled = request.RepairDispatchEnabled,
        AutomaticMergeEnabled = request.AutomaticMergeEnabled,
        SignalProfileVersion = request.SignalProfileVersion,
        DefaultAttemptLimit = request.DefaultAttemptLimit,
        VerificationWindow = request.VerificationWindow,
        TimeBudget = request.TimeBudget,
        ConcurrencyBudget = request.ConcurrencyBudget,
        InferenceBudget = request.InferenceBudget,
        RepositoryRunBudget = request.RepositoryRunBudget,
        ClassificationPolicyJson = request.ClassificationPolicyJson,
        ApplicationKillSwitch = existing?.ApplicationKillSwitch ?? false,
        CreatedAt = existing?.CreatedAt ?? default,
        Version = existing?.Version ?? [],
        Environments = request.Environments.Select(x => new HealingEnvironmentConfiguration
        {
            Id = existing?.Environments.SingleOrDefault(y => y.EnvironmentId == x.EnvironmentId)?.Id ?? Guid.Empty,
            EnvironmentId = x.EnvironmentId,
            DiscoveryEnabled = x.DiscoveryEnabled,
            RepairEnabled = x.RepairDispatchEnabled,
            OccurrenceThreshold = x.OccurrenceThreshold,
            DebounceWindow = x.DebounceWindow,
            ClassificationPolicyJson = x.ClassificationPolicyJson,
            EnvironmentKillSwitch = x.EnvironmentKillSwitch
        }).ToList()
    };

    private static async Task<object> ConfigurationResponseAsync(HttpContext context, WorkflowApplication application, HealingConfiguration configuration, CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, configuration.ApplicationId, cancellationToken);
        return new
        {
            configuration.ApplicationId,
            ApplicationName = application.Name,
            configuration.DiscoveryEnabled,
            RepairDispatchEnabled = configuration.RepairEnabled,
            configuration.AutomaticMergeEnabled,
            configuration.ApplicationKillSwitch,
            configuration.SignalProfileVersion,
            configuration.DefaultAttemptLimit,
            configuration.VerificationWindow,
            configuration.TimeBudget,
            configuration.ConcurrencyBudget,
            configuration.InferenceBudget,
            configuration.RepositoryRunBudget,
            configuration.ClassificationPolicyJson,
            Version = Convert.ToBase64String(configuration.Version),
            ManifestReadiness = await ManifestReadinessAsync(context, configuration.ApplicationId, authorization, cancellationToken),
            ProviderReadiness = await ProviderReadinessAsync(context, configuration.ApplicationId, authorization, cancellationToken),
            Environments = application.Environments.Where(x => Guid.TryParse(x.Id, out _)).Select(environment =>
            {
                var item = configuration.Environments.SingleOrDefault(x => x.EnvironmentId == Guid.Parse(environment.Id));
                return new
                {
                    EnvironmentId = Guid.Parse(environment.Id),
                    environment.Name,
                    DiscoveryEnabled = item?.DiscoveryEnabled ?? configuration.DiscoveryEnabled,
                    RepairDispatchEnabled = item?.RepairEnabled ?? configuration.RepairEnabled,
                    EnvironmentKillSwitch = item?.EnvironmentKillSwitch ?? false,
                    item?.OccurrenceThreshold,
                    item?.DebounceWindow,
                    ClassificationPolicyJson = item?.ClassificationPolicyJson ?? "{}"
                };
            }),
            Permissions = authorization.Permissions.Order()
        };
    }

    private static CoreManifest ToCoreManifest(Guid workspaceId, Guid applicationId, Guid revisionId, ContractManifest contract, string canonicalJson)
    {
        var manifestId = Guid.NewGuid();
        var entries = contract.Components.Select(component =>
        {
            var entryId = Guid.NewGuid();
            var kind = component.Kind.ToLowerInvariant() switch { "application" => ComponentKind.Application, "package" => ComponentKind.Package, "assembly" => ComponentKind.Assembly, _ => ComponentKind.Unknown };
            return new ValenceControl.Healing.Core.ComponentManifestEntry
            {
                Id = entryId,
                ManifestId = manifestId,
                WorkspaceId = workspaceId,
                ApplicationId = applicationId,
                ComponentKey = component.Key,
                Kind = kind,
                KindName = component.Kind,
                Name = component.Name,
                Version = component.Version,
                PackageId = kind == ComponentKind.Package ? component.Name : null,
                PackageVersion = kind == ComponentKind.Package ? component.Version : null,
                AssemblyName = kind == ComponentKind.Assembly ? component.Name : null,
                AssemblyVersion = kind == ComponentKind.Assembly ? component.Version : null,
                ContentHash = component.ContentHash,
                RepositoryUrl = component.RepositoryUrl,
                RepositoryCommit = component.RepositoryCommit,
                IsDirectDependency = component.DirectDependency,
                Assemblies = component.Assemblies.Select(assembly => new ComponentManifestAssemblyArtifact
                {
                    Id = Guid.NewGuid(), ManifestId = manifestId, ComponentEntryId = entryId, WorkspaceId = workspaceId, ApplicationId = applicationId,
                    Name = assembly.Name, Version = assembly.Version, PublicKeyToken = assembly.PublicKeyToken, RelativePath = assembly.RelativePath, ContentHash = assembly.ContentHash
                }).ToList()
            };
        }).ToList();
        var byKey = entries.ToDictionary(x => x.ComponentKey, StringComparer.Ordinal);
        var dependencies = contract.Components.SelectMany(component => component.Dependencies.Select(dependency => new ComponentDependency
        {
            Id = Guid.NewGuid(), ManifestId = manifestId, FromEntryId = byKey[component.Key].Id, ToEntryId = byKey[dependency].Id
        })).ToList();
        return new CoreManifest
        {
            Id = manifestId, WorkspaceId = workspaceId, ApplicationId = applicationId, RevisionId = revisionId,
            SchemaVersion = contract.SchemaVersion, SourceRevision = contract.Revision.SourceRevision, BuildId = contract.Revision.BuildId,
            ManifestDigest = contract.ManifestDigest!, CanonicalJson = canonicalJson, CreatedAt = contract.Revision.CreatedAt,
            Entries = entries, Dependencies = dependencies
        };
    }

    private static async Task<object> ProjectManifestAsync(CoreManifest manifest, SourceOwnershipService ownership, HealingAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolutions = new Dictionary<Guid, SourceOwnershipResolution>();
        var bindings = await ownership.ListAsync(manifest.WorkspaceId, manifest.ApplicationId, authorization, cancellationToken);
        if (ComponentManifestService.IsAutomationAuthoritative(manifest))
        {
            foreach (var component in manifest.Entries.Where(x => x.Kind != ComponentKind.Unknown))
                resolutions[component.Id] = await ownership.ResolveAsync(manifest.WorkspaceId, manifest.ApplicationId, component, authorization, cancellationToken);
        }
        return ToManifestResponse(manifest, bindings, resolutions);
    }

    private static object ToManifestResponse(CoreManifest manifest, IReadOnlyList<SourceOwnershipBinding> bindings, IReadOnlyDictionary<Guid, SourceOwnershipResolution>? resolutions = null) => new
    {
        manifest.Id, manifest.RevisionId, manifest.SourceRevision, manifest.ManifestDigest, manifest.TrustState,
        manifest.VerificationMethod, AutomationAuthoritative = ComponentManifestService.IsAutomationAuthoritative(manifest), manifest.CreatedAt,
        Dependencies = manifest.Dependencies.Select(dependency => new
        {
            FromComponentKey = manifest.Entries.Single(x => x.Id == dependency.FromEntryId).ComponentKey,
            ToComponentKey = manifest.Entries.Single(x => x.Id == dependency.ToEntryId).ComponentKey
        }),
        Entries = manifest.Entries.Select(component =>
        {
            var matches = bindings.Where(x => x.Status == SourceOwnershipBindingStatus.Active && SourceOwnershipService.Matches(x, component)).ToArray();
            SourceOwnershipResolution? governedResolution = null;
            resolutions?.TryGetValue(component.Id, out governedResolution);
            var ambiguous = governedResolution?.Status == SourceOwnershipResolutionStatus.Ambiguous || matches.Length > 1 && matches.Skip(1).Any(x => !SourceOwnershipService.HasSameAuthority(matches[0], x));
            var selected = governedResolution?.SelectedBinding ?? (matches.Length > 0 && !ambiguous ? matches.OrderByDescending(x => x.Priority).ThenBy(x => x.Id).First() : null);
            var resolution = selected is not null ? "Selected" : ambiguous ? "Ambiguous" : component.RepositoryUrl is not null ? "Suggested" : "Unmapped";
            return new
            {
                component.ComponentKey, Kind = component.KindName, component.Name, component.Version, component.ContentHash,
                RepositorySuggestion = component.RepositoryUrl, BindingId = selected?.Id,
                Assemblies = component.Assemblies.Select(assembly => new
                {
                    assembly.Name, assembly.Version, assembly.PublicKeyToken, assembly.RelativePath, assembly.ContentHash
                }),
                MatchingBindings = (governedResolution?.MatchingBindings ?? matches).Select(binding => new
                {
                    binding.Id, binding.Name, binding.Priority,
                    Repository = $"{binding.RepositoryOwner}/{binding.RepositoryName}",
                    binding.TargetBranch, binding.WorkflowIdentity, binding.WorkflowReference, binding.Status
                }),
                ReasonCodes = governedResolution?.ReasonCodes ?? (ambiguous
                    ? [HealingOwnershipReasonCodes.AmbiguousAuthority]
                    : selected is null ? [HealingOwnershipReasonCodes.NoApprovedBinding] : []),
                OwnershipResolution = resolution,
                RepairEligibility = !ComponentManifestService.IsAutomationAuthoritative(manifest) || component.Kind == ComponentKind.Unknown ? "ObservationOnly" : selected is not null ? "Repairable" : ambiguous ? "Ambiguous" : "Unauthorized"
            };
        })
    };

    private static SourceOwnershipBinding ToBinding(Guid workspaceId, Guid applicationId, Guid bindingId, SourceOwnershipBindingRequest request) => new()
    {
        Id = bindingId, WorkspaceId = workspaceId, ApplicationId = applicationId, Name = request.Name,
        SelectorKind = request.SelectorKind, SelectorPattern = request.SelectorPattern, Priority = request.Priority,
        ProviderConnectionId = request.ProviderConnectionId, RepositoryProviderId = request.RepositoryProviderId,
        RepositoryOwner = request.RepositoryOwner, RepositoryName = request.RepositoryName, TargetBranch = request.TargetBranch,
        WorkflowIdentity = request.WorkflowIdentity, WorkflowReference = request.WorkflowReference!, WorkflowRevision = request.WorkflowRevision,
        PathPolicyId = request.PathPolicyId, EvidencePolicyId = request.EvidencePolicyId, MergePolicyId = request.MergePolicyId,
        Status = SourceOwnershipBindingStatus.Draft
    };

    private static object ToBindingResponse(SourceOwnershipBinding binding) => new
    {
        binding.Id, binding.Name, binding.SelectorKind, binding.SelectorPattern,
        Repository = $"{binding.RepositoryOwner}/{binding.RepositoryName}", binding.TargetBranch, binding.WorkflowIdentity, binding.WorkflowReference, binding.Status,
        Version = Convert.ToBase64String(binding.Version)
    };

    private static async Task<string> ManifestReadinessAsync(HttpContext context, Guid applicationId, HealingAuthorization authorization, CancellationToken cancellationToken)
    {
        var manifests = await context.RequestServices.GetRequiredService<ComponentManifestService>()
            .ListAsync(authorization.WorkspaceId, applicationId, authorization, cancellationToken);
        if (manifests.Count == 0)
            return "Missing";
        var authoritative = manifests.Where(ComponentManifestService.IsAutomationAuthoritative).ToArray();
        if (authoritative.Length == 0)
            return "Untrusted";
        var currentRevisionIds = (await context.RequestServices.GetRequiredService<IWorkspaceDeploymentStore>()
                .ListApplicationRevisionsAsync(authorization.WorkspaceId, applicationId, cancellationToken))
            .Where(x => x.IsCurrentDesired || x.IsCurrentDeployed)
            .Select(x => x.Revision.Id)
            .ToHashSet();
        if (currentRevisionIds.Count == 0)
            return "Stale";
        var authoritativeRevisionIds = authoritative.Select(x => x.RevisionId).ToHashSet();
        return currentRevisionIds.IsSubsetOf(authoritativeRevisionIds) ? "Ready" : "Stale";
    }

    private static async Task<string> ProviderReadinessAsync(HttpContext context, Guid applicationId, HealingAuthorization authorization, CancellationToken cancellationToken)
    {
        var bindings = await context.RequestServices.GetRequiredService<SourceOwnershipService>()
            .ListAsync(authorization.WorkspaceId, applicationId, authorization, cancellationToken);
        var active = bindings.Where(x => x.Status == SourceOwnershipBindingStatus.Active).ToArray();
        if (active.Length == 0)
            return "Missing";
        var store = context.RequestServices.GetRequiredService<IHealingOwnershipStore>();
        foreach (var binding in active)
        {
            var provider = await store.GetProviderConnectionAsync(authorization.WorkspaceId, binding.ProviderConnectionId, cancellationToken);
            if (provider?.Status != ProviderConnectionStatus.Active)
                return "Unavailable";
        }
        return "Ready";
    }

    private static IResult OperationFailure(HttpContext context, string reasonCode) => reasonCode switch
    {
        HealingOwnershipReasonCodes.NotFound => HealingProblem(context, StatusCodes.Status404NotFound, $"healing.{reasonCode}", "The requested Healing resource was not found."),
        HealingOwnershipReasonCodes.Unauthorized or HealingOwnershipReasonCodes.OwnerApprovalRequired or HealingOwnershipReasonCodes.AutomaticMergePermissionRequired => HealingProblem(context, StatusCodes.Status403Forbidden, $"healing.{reasonCode}", "The requested Healing operation is not permitted."),
        HealingOwnershipReasonCodes.IdempotencyConflict => HealingProblem(context, StatusCodes.Status409Conflict, "healing.idempotency-key.conflict", "Idempotency-Key was already used with a different request payload."),
        HealingOwnershipReasonCodes.ImmutableRevisionConflict or HealingOwnershipReasonCodes.AmbiguousAuthority => HealingProblem(context, StatusCodes.Status409Conflict, $"healing.{reasonCode}", "The requested Healing operation conflicts with existing state."),
        _ => HealingProblem(context, StatusCodes.Status400BadRequest, $"healing.{reasonCode}", "The requested Healing operation is invalid.")
    };

    internal static IResult HealingProblem(HttpContext context, int status, string code, string detail) => Results.Problem(
        title: "Healing request failed.", detail: detail, statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = context.TraceIdentifier });

    internal static string Sha256(string value) => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static bool ValidWorkflowReference(string? value) =>
        ValidRequired(value, MaxKeyLength) &&
        (value!.StartsWith("refs/heads/", StringComparison.Ordinal) ||
         value.StartsWith("refs/tags/", StringComparison.Ordinal));

    private static IResult? ValidateBindingRequest(HttpContext context, SourceOwnershipBindingRequest request, bool requireVersion, out byte[] version)
    {
        version = [];
        if (!Enum.IsDefined(request.SelectorKind) ||
            !ValidRequired(request.Name, MaxNameLength) ||
            !ValidRequired(request.SelectorPattern, MaxPathLength) ||
            !ValidRequired(request.RepositoryProviderId, MaxKeyLength) ||
            !ValidRequired(request.RepositoryOwner, MaxNameLength) ||
            !ValidRequired(request.RepositoryName, MaxNameLength) ||
            !ValidRequired(request.TargetBranch, MaxKeyLength) ||
            !ValidRequired(request.WorkflowIdentity, MaxPathLength) ||
            !ValidWorkflowReference(request.WorkflowReference) ||
            !ValidRequired(request.WorkflowRevision, MaxKeyLength) ||
            request.ProviderConnectionId == Guid.Empty || request.PathPolicyId == Guid.Empty ||
            request.EvidencePolicyId == Guid.Empty || request.MergePolicyId == Guid.Empty)
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.binding.invalid", "The source ownership binding contains an invalid or oversized field.");
        if (!requireVersion)
            return null;
        if (string.IsNullOrWhiteSpace(request.Version))
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.binding.version.required", "The source ownership binding version is required.");
        if (request.Version.Length > 64)
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.binding.version.invalid", "The source ownership binding version is invalid.");
        try
        {
            version = Convert.FromBase64String(request.Version);
        }
        catch (FormatException)
        {
            return HealingProblem(context, StatusCodes.Status400BadRequest, "healing.binding.version.invalid", "The source ownership binding version is invalid.");
        }
        return version.Length == 16
            ? null
            : HealingProblem(context, StatusCodes.Status400BadRequest, "healing.binding.version.invalid", "The source ownership binding version is invalid.");
    }

    private static bool ValidRequired(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool TryGetSingleHeader(HttpContext context, string name, out string value)
    {
        var values = context.Request.Headers[name];
        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return values.Count == 1;
    }

    private static bool IsSha256Digest(string value) =>
        value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) && !value.AsSpan(7).ContainsAnyExcept("0123456789abcdef");

    private static async Task<(string? Body, bool TooLarge)> ReadManifestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxManifestBodyBytes)
            return (null, true);
        using var body = new MemoryStream(request.ContentLength is > 0 and <= MaxManifestBodyBytes ? (int)request.ContentLength.Value : 0);
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (body.Length + read > MaxManifestBodyBytes)
                return (null, true);
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        try
        {
            return (new UTF8Encoding(false, true).GetString(body.GetBuffer(), 0, (int)body.Length), false);
        }
        catch (DecoderFallbackException)
        {
            return (null, false);
        }
    }
    private static string EmergencyStopTarget(Guid applicationId) => $"{applicationId:D}:emergency-stop";
    private static string EmergencyResumeTarget(Guid applicationId) => $"{applicationId:D}:emergency-resume";
    internal static string AutomaticMergeTarget(Guid applicationId, bool enabled) => $"{applicationId:D}:automatic-merge:{enabled.ToString().ToLowerInvariant()}";
}

public static class HealingPermissionEndpointFilters
{
    internal const string EffectivePermissionsItemKey = "ValenceControl.Healing.EffectiveWorkspacePermissions";

    public static RouteHandlerBuilder RequireHealingPermission(this RouteHandlerBuilder builder, string permission) => builder.AddEndpointFilter(async (invocation, next) =>
    {
        var context = invocation.HttpContext;
        var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(context, WorkspaceOperation.Read);
        if (denied is not null)
            return denied;
        var permissions = context.RequestServices.GetRequiredService<WorkspacePermissionService>();
        var effective = await permissions.GetEffectiveHealingPermissionsAsync(context.GetWorkspaceAccess(), context.RequestAborted);
        context.Items[EffectivePermissionsItemKey] = effective;
        return effective.Has(permission) ? await next(invocation) : HealingPermissionDenied(context, permission);
    });

    public static IResult HealingPermissionDenied(HttpContext context, string permission) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(context, StatusCodes.Status403Forbidden, "healing.permission-required", $"Workspace permission '{permission}' is required.");

    public static Task<EffectiveWorkspacePermissions> GetEffectiveHealingPermissionsAsync(this WorkspacePermissionService permissions, WorkspaceAccess access, CancellationToken cancellationToken) =>
        permissions.GetEffectivePermissionsAsync(access.WorkspaceId, access.AccountId, cancellationToken);
}

public sealed record HealingConfirmationRequest(ConfirmationActionType ActionType, bool? AutomaticMergeEnabled);
public sealed record HealingStopRequest(Guid ConfirmationId);
public sealed record HealingEnvironmentConfigurationRequest(Guid EnvironmentId, bool DiscoveryEnabled, bool RepairDispatchEnabled, bool EnvironmentKillSwitch, int? OccurrenceThreshold, TimeSpan? DebounceWindow, string ClassificationPolicyJson = "{}");
public sealed record UpdateHealingConfigurationRequest(bool DiscoveryEnabled, bool RepairDispatchEnabled, bool AutomaticMergeEnabled, string SignalProfileVersion, int DefaultAttemptLimit, TimeSpan VerificationWindow, TimeSpan TimeBudget, int ConcurrencyBudget, long InferenceBudget, int RepositoryRunBudget, IReadOnlyList<HealingEnvironmentConfigurationRequest> Environments, string Version, Guid? ConfirmationId, string ClassificationPolicyJson = "{}");
public sealed record SourceOwnershipBindingRequest(string Name, SourceSelectorKind SelectorKind, string SelectorPattern, int Priority, Guid ProviderConnectionId, string RepositoryProviderId, string RepositoryOwner, string RepositoryName, string TargetBranch, string WorkflowIdentity, string WorkflowRevision, Guid PathPolicyId, Guid EvidencePolicyId, Guid MergePolicyId, string? Version = null, string? WorkflowReference = null);
