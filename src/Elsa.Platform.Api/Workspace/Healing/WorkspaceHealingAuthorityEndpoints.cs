using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed class WorkspaceHealingAuthorityEndpointModule : IHealingEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing/applications/{applicationId:guid}");
        group.AddEndpointFilter(RequireApplicationAsync);
        group.MapGet("/authority-catalog", ListAsync)
            .RequireHealingPermission(HealingPermissions.Read);
        group.MapPost("/authority-profiles", CreateAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/provider-connections/{providerConnectionId:guid}/{transition}", TransitionProviderAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
        group.MapPost("/provider-connections/{providerConnectionId:guid}/validate", ValidateProviderAsync)
            .RequireHealingPermission(HealingPermissions.Configure);
    }

    private static async ValueTask<object?> RequireApplicationAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(context, WorkspaceOperation.Read);
        if (denied is not null)
            return denied;
        if (!Guid.TryParse(context.Request.RouteValues["workspaceId"]?.ToString(), out var workspaceId) ||
            !Guid.TryParse(context.Request.RouteValues["applicationId"]?.ToString(), out var applicationId))
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.");
        var cockpit = context.RequestServices.GetRequiredService<DeploymentCockpitService>();
        var application = (await cockpit.GetCockpitAsync(workspaceId, context.RequestAborted)).Applications
            .SingleOrDefault(x => string.Equals(x.Id, applicationId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        return application is null
            ? WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status404NotFound, "healing.application.not-found", "Application was not found.")
            : await next(invocation);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        HttpContext context,
        WorkspacePermissionService permissions,
        HealingAdministrationService service,
        CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, permissions, cancellationToken);
        var result = await service.ListAsync(workspaceId, applicationId, authorization, cancellationToken);
        return result.Succeeded
            ? Results.Ok(ToCatalogResponse(result.Value!))
            : OperationFailure(context, result.ReasonCode);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        Guid applicationId,
        CreateHealingAuthorityProfileRequest request,
        HttpContext context,
        WorkspacePermissionService permissions,
        ConfirmationService confirmations,
        HealingAdministrationService service,
        CancellationToken cancellationToken)
    {
        var authorization = await CreateAuthorizationAsync(context, applicationId, permissions, cancellationToken);
        if (request.AutomaticMergeEnabled == true)
        {
            if (!authorization.Permissions.Contains(HealingPermissions.ConfigureAutoMerge))
                return HealingPermissionEndpointFilters.HealingPermissionDenied(context, HealingPermissions.ConfigureAutoMerge);
            if (request.ConfirmationId is null)
                return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                    context, StatusCodes.Status400BadRequest, "deployment.confirmation.missing", "An automatic-merge confirmation is required.");
            var confirmation = await confirmations.ConsumeConfirmationAsync(
                workspaceId,
                request.ConfirmationId.Value,
                context.GetWorkspaceAccess().AccountId,
                ConfirmationActionType.HealingAutomaticMerge,
                WorkspaceHealingConfigurationEndpointModule.AutomaticMergeTarget(applicationId, true),
                cancellationToken);
            if (!confirmation.Succeeded)
                return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                    context, StatusCodes.Status400BadRequest, confirmation.Validation.Id, confirmation.Validation.Message);
        }
        var result = await service.CreateProfileAsync(
            workspaceId,
            applicationId,
            new CreateHealingAuthorityProfile(
                request.Name,
                request.InstallationId,
                request.RepositoryOwner,
                request.RepositoryName,
                request.CredentialReferenceId,
                request.AllowedRoots ?? ["src", "tests"],
                request.ForbiddenRoots ?? [".github", ".azure", "eng", "scripts"],
                request.MaxFiles ?? 20,
                request.MaxChangedLines ?? 1_000,
                request.MaxPatchBytes ?? 1_000_000,
                request.RequireReproduction ?? false,
                request.AllowHighConfidenceInference ?? true,
                request.MinimumInferenceConfidence ?? 0.9m,
                request.AutomaticMergeEnabled ?? false,
                request.RequiredChecks ?? [],
                request.IndependentVerifier,
                request.ForbiddenChangeCategories ?? ["workflow", "build-infrastructure", "credentials"],
                request.RequireRollbackOrStopCapability ?? true,
                request.WebhookSecretCredentialReferenceId),
            authorization,
            cancellationToken);
        return result.Succeeded
            ? Results.Created(
                $"/api/workspaces/{workspaceId:D}/healing/applications/{applicationId:D}/authority-catalog",
                ToProfileResponse(result.Value!))
            : OperationFailure(context, result.ReasonCode);
    }

    private static async Task<IResult> TransitionProviderAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid providerConnectionId,
        string transition,
        ProviderConnectionTransitionRequest request,
        HttpContext context,
        WorkspacePermissionService permissions,
        HealingAdministrationService service,
        CancellationToken cancellationToken)
    {
        var target = transition.ToLowerInvariant() switch
        {
            "suspend" => ProviderConnectionStatus.Suspended,
            "revoke" => ProviderConnectionStatus.Revoked,
            _ => (ProviderConnectionStatus?)null
        };
        if (target is null)
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status404NotFound, "healing.provider.transition", "Provider transition was not found.");
        if (!TryDecodeVersion(request.Version, out var version))
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status400BadRequest, "healing.provider.version", "Provider version is invalid.");
        try
        {
            var result = await service.TransitionProviderAsync(
                workspaceId, applicationId, providerConnectionId, target.Value, version,
                await CreateAuthorizationAsync(context, applicationId, permissions, cancellationToken), cancellationToken);
            return result.Succeeded ? Results.Ok(ToProviderResponse(result.Value!)) : OperationFailure(context, result.ReasonCode);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status409Conflict, "healing.provider.stale", "The provider connection changed after it was loaded.");
        }
    }

    private static async Task<IResult> ValidateProviderAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid providerConnectionId,
        ProviderConnectionTransitionRequest request,
        HttpContext context,
        WorkspacePermissionService permissions,
        HealingAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeVersion(request.Version, out var version))
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status400BadRequest, "healing.provider.version", "Provider version is invalid.");
        try
        {
            var result = await service.ValidateProviderAsync(
                workspaceId, applicationId, providerConnectionId, version,
                await CreateAuthorizationAsync(context, applicationId, permissions, cancellationToken), cancellationToken);
            return result.Succeeded ? Results.Ok(ToProviderResponse(result.Value!)) : OperationFailure(context, result.ReasonCode);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return WorkspaceHealingConfigurationEndpointModule.HealingProblem(
                context, StatusCodes.Status409Conflict, "healing.provider.stale", "The provider connection changed after it was loaded.");
        }
    }

    private static bool TryDecodeVersion(string? value, out byte[] version)
    {
        version = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            return false;
        try
        {
            version = Convert.FromBase64String(value);
            return version.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<HealingAuthorization> CreateAuthorizationAsync(
        HttpContext context,
        Guid applicationId,
        WorkspacePermissionService permissions,
        CancellationToken cancellationToken)
    {
        var access = context.GetWorkspaceAccess();
        var effective = context.Items[HealingPermissionEndpointFilters.EffectivePermissionsItemKey] as EffectiveWorkspacePermissions
            ?? await permissions.GetEffectiveHealingPermissionsAsync(access, cancellationToken);
        return new HealingAuthorization(
            access.WorkspaceId,
            applicationId,
            access.AccountId.ToString("D"),
            access.Role == WorkspaceRole.Owner,
            effective.Permissions);
    }

    private static object ToCatalogResponse(HealingAuthorityCatalog catalog) => new
    {
        ProviderConnections = catalog.ProviderConnections.Select(ToProviderResponse),
        PathPolicies = catalog.PathPolicies.Select(ToPathPolicyResponse),
        EvidencePolicies = catalog.EvidencePolicies.Select(ToEvidencePolicyResponse),
        MergePolicies = catalog.MergePolicies.Select(ToMergePolicyResponse)
    };

    private static object ToProfileResponse(HealingAuthorityProfile profile) => new
    {
        ProviderConnection = ToProviderResponse(profile.ProviderConnection),
        PathPolicy = ToPathPolicyResponse(profile.PathPolicy),
        EvidencePolicy = ToEvidencePolicyResponse(profile.EvidencePolicy),
        MergePolicy = ToMergePolicyResponse(profile.MergePolicy)
    };

    private static object ToProviderResponse(ProviderConnection provider) => new
    {
        provider.Id,
        provider.Provider,
        provider.InstallationId,
        provider.RepositoryProviderId,
        provider.RepositoryOwner,
        provider.RepositoryName,
        provider.Status,
        provider.UpdatedAt,
        Version = Convert.ToBase64String(provider.Version)
    };

    private static object ToPathPolicyResponse(PathPolicy policy) => new
    {
        policy.Id, policy.Name, policy.PolicyVersion, policy.PolicyHash,
        policy.AllowedRootsJson, policy.ForbiddenRootsJson,
        policy.MaxFiles, policy.MaxChangedLines, policy.MaxPatchBytes
    };

    private static object ToEvidencePolicyResponse(EvidencePolicy policy) => new
    {
        policy.Id, policy.Name, policy.PolicyVersion, policy.PolicyHash,
        policy.RequireReproduction, policy.AllowHighConfidenceInference,
        policy.MinimumInferenceConfidence, policy.MaximumTier
    };

    private static object ToMergePolicyResponse(MergePolicy policy) => new
    {
        policy.Id, policy.Name, policy.PolicyVersion, policy.PolicyHash,
        policy.AutomaticMergeEnabled, policy.RequiredChecksJson,
        policy.IndependentVerifier, policy.ForbiddenChangeCategoriesJson,
        policy.RequireRollbackOrStopCapability
    };

    private static IResult OperationFailure(HttpContext context, string reasonCode) =>
        WorkspaceHealingConfigurationEndpointModule.HealingProblem(
            context,
            reasonCode switch
            {
                HealingOwnershipReasonCodes.Unauthorized or HealingOwnershipReasonCodes.OwnerApprovalRequired => StatusCodes.Status403Forbidden,
                HealingOwnershipReasonCodes.ImmutableRevisionConflict or HealingOwnershipReasonCodes.AdministrationConflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            },
            $"healing.{reasonCode}",
            reasonCode switch
            {
                HealingOwnershipReasonCodes.OwnerApprovalRequired => "Workspace owner approval is required.",
                HealingOwnershipReasonCodes.AdministrationConflict => "A Healing authority profile with the same identity already exists.",
                HealingOwnershipReasonCodes.ProviderValidationFailed => "The GitHub App installation or repository could not be validated.",
                HealingOwnershipReasonCodes.ProviderRepositoryMismatch => "The validated GitHub repository does not match the authorized immutable identity.",
                _ => "The Healing authority profile is invalid."
            });
}

public sealed record CreateHealingAuthorityProfileRequest(
    string? Name,
    string? InstallationId,
    string? RepositoryOwner,
    string? RepositoryName,
    Guid CredentialReferenceId,
    IReadOnlyList<string>? AllowedRoots = null,
    IReadOnlyList<string>? ForbiddenRoots = null,
    int? MaxFiles = null,
    int? MaxChangedLines = null,
    int? MaxPatchBytes = null,
    bool? RequireReproduction = null,
    bool? AllowHighConfidenceInference = null,
    decimal? MinimumInferenceConfidence = null,
    bool? AutomaticMergeEnabled = null,
    IReadOnlyList<string>? RequiredChecks = null,
    string? IndependentVerifier = null,
    IReadOnlyList<string>? ForbiddenChangeCategories = null,
    bool? RequireRollbackOrStopCapability = null,
    Guid? WebhookSecretCredentialReferenceId = null,
    Guid? ConfirmationId = null);

public sealed record ProviderConnectionTransitionRequest(string? Version);
