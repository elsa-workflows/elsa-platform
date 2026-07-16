using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Platform.Healing.Core.Security;

namespace Elsa.Platform.Healing.Core.Ownership;

public sealed record HealingAuthorityCatalog(
    IReadOnlyList<ProviderConnection> ProviderConnections,
    IReadOnlyList<PathPolicy> PathPolicies,
    IReadOnlyList<EvidencePolicy> EvidencePolicies,
    IReadOnlyList<MergePolicy> MergePolicies);

public sealed record CreateHealingAuthorityProfile(
    string? Name,
    string? InstallationId,
    string? RepositoryOwner,
    string? RepositoryName,
    Guid CredentialReferenceId,
    IReadOnlyList<string>? AllowedRoots,
    IReadOnlyList<string>? ForbiddenRoots,
    int MaxFiles,
    int MaxChangedLines,
    int MaxPatchBytes,
    bool RequireReproduction,
    bool AllowHighConfidenceInference,
    decimal MinimumInferenceConfidence,
    bool AutomaticMergeEnabled,
    IReadOnlyList<string>? RequiredChecks,
    string? IndependentVerifier,
    IReadOnlyList<string>? ForbiddenChangeCategories,
    bool RequireRollbackOrStopCapability);

public sealed record HealingAuthorityProfile(
    ProviderConnection ProviderConnection,
    PathPolicy PathPolicy,
    EvidencePolicy EvidencePolicy,
    MergePolicy MergePolicy);

public sealed class HealingAdministrationConflictException(string message) : InvalidOperationException(message);

public sealed class HealingAdministrationService(
    IHealingOwnershipStore ownershipStore,
    IHealingAdministrationStore administrationStore,
    IProviderConnectionValidator providerValidator,
    HealingAuditService auditService,
    TimeProvider? timeProvider = null)
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafePath = new("^(?!/)(?!.*(?:^|/)\\.\\.(?:/|$))[A-Za-z0-9_.@/\\-]{1,240}$", RegexOptions.CultureInvariant);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<HealingOperationResult<HealingAuthorityCatalog>> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingAuthorityCatalog>.Denied(authorizationFailure);

        var providers = await administrationStore.ListProviderConnectionsAsync(workspaceId, cancellationToken);
        var pathPolicies = await administrationStore.ListPathPoliciesAsync(workspaceId, applicationId, cancellationToken);
        var evidencePolicies = await administrationStore.ListEvidencePoliciesAsync(workspaceId, applicationId, cancellationToken);
        var mergePolicies = await administrationStore.ListMergePoliciesAsync(workspaceId, applicationId, cancellationToken);
        return HealingOperationResult<HealingAuthorityCatalog>.Success(
            new HealingAuthorityCatalog(providers, pathPolicies, evidencePolicies, mergePolicies));
    }

    public async ValueTask<HealingOperationResult<HealingAuthorityProfile>> CreateProfileAsync(
        Guid workspaceId,
        Guid applicationId,
        CreateHealingAuthorityProfile request,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(
            authorization, workspaceId, applicationId, request.AutomaticMergeEnabled);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingAuthorityProfile>.Denied(authorizationFailure);
        if (!IsValid(request))
            return HealingOperationResult<HealingAuthorityProfile>.Denied(HealingOwnershipReasonCodes.InvalidConfiguration);

        var now = _timeProvider.GetUtcNow();
        var providerId = Guid.NewGuid();
        var provider = new ProviderConnection
        {
            Id = providerId, WorkspaceId = workspaceId, Provider = "GitHub",
            InstallationId = request.InstallationId!.Trim(), RepositoryProviderId = $"pending-{providerId:N}",
            RepositoryOwner = request.RepositoryOwner!.Trim(), RepositoryName = request.RepositoryName!.Trim(),
            CredentialReference = $"credential://{request.CredentialReferenceId:D}", Status = ProviderConnectionStatus.PendingValidation,
            CreatedAt = now, UpdatedAt = now
        };
        var pathPolicy = new PathPolicy
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
            Name = $"{request.Name!.Trim()} paths", PolicyVersion = "1",
            AllowedRootsJson = JsonSerializer.Serialize(request.AllowedRoots!),
            ForbiddenRootsJson = JsonSerializer.Serialize(request.ForbiddenRoots!),
            MaxFiles = request.MaxFiles, MaxChangedLines = request.MaxChangedLines, MaxPatchBytes = request.MaxPatchBytes,
            AllowBinary = false, AllowRenames = false, AllowSymlinks = false, AllowSubmodules = false, CreatedAt = now
        };
        var evidencePolicy = new EvidencePolicy
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
            Name = $"{request.Name!.Trim()} evidence", PolicyVersion = "1",
            RequireReproduction = request.RequireReproduction,
            AllowHighConfidenceInference = request.AllowHighConfidenceInference,
            MinimumInferenceConfidence = request.MinimumInferenceConfidence,
            MaximumTier = EvidenceTier.DefaultRedacted,
            PermittedFieldsJson = "[]", CreatedAt = now
        };
        var mergePolicy = new MergePolicy
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
            Name = $"{request.Name!.Trim()} merge", PolicyVersion = "1",
            AutomaticMergeEnabled = request.AutomaticMergeEnabled,
            RequiredChecksJson = JsonSerializer.Serialize(request.RequiredChecks!),
            IndependentVerifier = string.IsNullOrWhiteSpace(request.IndependentVerifier) ? null : request.IndependentVerifier.Trim(),
            ForbiddenChangeCategoriesJson = JsonSerializer.Serialize(request.ForbiddenChangeCategories!),
            RequireRollbackOrStopCapability = request.RequireRollbackOrStopCapability, CreatedAt = now
        };
        pathPolicy.PolicyHash = PolicyHash(pathPolicy);
        evidencePolicy.PolicyHash = PolicyHash(evidencePolicy);
        mergePolicy.PolicyHash = PolicyHash(mergePolicy);

        HealingAuthorityProfile profile;
        try
        {
            profile = await ownershipStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                var persistedProvider = await administrationStore.SaveProviderConnectionAsync(provider, transactionCancellationToken);
                await administrationStore.SavePoliciesAsync(pathPolicy, evidencePolicy, mergePolicy, transactionCancellationToken);
                await AppendAuditAsync(workspaceId, applicationId, persistedProvider.Id, "provider-connection-authorized", authorization, request, transactionCancellationToken);
                await AppendAuditAsync(workspaceId, applicationId, pathPolicy.Id, "repair-policies-created", authorization, request, transactionCancellationToken);
                return new HealingAuthorityProfile(persistedProvider, pathPolicy, evidencePolicy, mergePolicy);
            }, cancellationToken);
        }
        catch (HealingAdministrationConflictException)
        {
            return HealingOperationResult<HealingAuthorityProfile>.Denied(HealingOwnershipReasonCodes.AdministrationConflict);
        }
        return HealingOperationResult<HealingAuthorityProfile>.Success(profile);
    }

    public async ValueTask<HealingOperationResult<ProviderConnection>> ValidateProviderAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid providerConnectionId,
        byte[] expectedVersion,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<ProviderConnection>.Denied(authorizationFailure);
        if (providerConnectionId == Guid.Empty || expectedVersion is not { Length: > 0 })
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.InvalidConfiguration);
        var provider = await ownershipStore.GetProviderConnectionAsync(workspaceId, providerConnectionId, cancellationToken);
        if (provider is null)
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (provider.Status is not (ProviderConnectionStatus.PendingValidation or ProviderConnectionStatus.Suspended))
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.InvalidBindingTransition);

        var validation = await providerValidator.ValidateAsync(provider, cancellationToken);
        if (!validation.Succeeded || string.IsNullOrWhiteSpace(validation.RepositoryProviderId) ||
            !IsSafeIdentifier(validation.RepositoryProviderId))
            return HealingOperationResult<ProviderConnection>.Denied(
                string.IsNullOrWhiteSpace(validation.ReasonCode) ? HealingOwnershipReasonCodes.ProviderValidationFailed : validation.ReasonCode);
        if (provider.Status == ProviderConnectionStatus.Suspended &&
            !string.Equals(provider.RepositoryProviderId, validation.RepositoryProviderId.Trim(), StringComparison.Ordinal))
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.ProviderRepositoryMismatch);

        provider.Version = expectedVersion;
        provider.RepositoryProviderId = validation.RepositoryProviderId.Trim();
        provider.Status = ProviderConnectionStatus.Active;
        provider.UpdatedAt = _timeProvider.GetUtcNow();
        try
        {
            var persisted = await ownershipStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                var saved = await administrationStore.SaveProviderConnectionAsync(provider, transactionCancellationToken);
                await auditService.AppendAsync(new HealingAuditWrite(
                    workspaceId, "provider-connection", providerConnectionId, "provider-connection-validated",
                    HealingOwnershipReasonCodes.Succeeded, "workspace-account", authorization.ActorId,
                    Guid.NewGuid(), null, null, null, null,
                    new Dictionary<string, string?>
                    {
                        ["repositoryOwner"] = provider.RepositoryOwner,
                        ["repositoryName"] = provider.RepositoryName,
                        ["status"] = "active"
                    }), transactionCancellationToken);
                return saved;
            }, cancellationToken);
            return HealingOperationResult<ProviderConnection>.Success(persisted);
        }
        catch (HealingAdministrationConflictException)
        {
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.AdministrationConflict);
        }
    }

    public async ValueTask<HealingOperationResult<ProviderConnection>> TransitionProviderAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid providerConnectionId,
        ProviderConnectionStatus target,
        byte[] expectedVersion,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<ProviderConnection>.Denied(authorizationFailure);
        if (providerConnectionId == Guid.Empty || expectedVersion is not { Length: > 0 })
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.InvalidConfiguration);
        var provider = await ownershipStore.GetProviderConnectionAsync(workspaceId, providerConnectionId, cancellationToken);
        if (provider is null)
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (provider.Status == ProviderConnectionStatus.Revoked ||
            provider.Status == ProviderConnectionStatus.PendingValidation && target != ProviderConnectionStatus.Revoked ||
            target == provider.Status ||
            target is not (ProviderConnectionStatus.Suspended or ProviderConnectionStatus.Revoked))
            return HealingOperationResult<ProviderConnection>.Denied(HealingOwnershipReasonCodes.InvalidBindingTransition);

        provider.Version = expectedVersion;
        provider.Status = target;
        provider.UpdatedAt = _timeProvider.GetUtcNow();
        var persisted = await ownershipStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var saved = await administrationStore.SaveProviderConnectionAsync(provider, transactionCancellationToken);
            await auditService.AppendAsync(new HealingAuditWrite(
                workspaceId, "provider-connection", providerConnectionId, "provider-connection-transitioned",
                HealingOwnershipReasonCodes.Succeeded, "workspace-account", authorization.ActorId,
                Guid.NewGuid(), null, null, null, null,
                new Dictionary<string, string?> { ["status"] = target.ToString().ToLowerInvariant() }),
                transactionCancellationToken);
            return saved;
        }, cancellationToken);
        return HealingOperationResult<ProviderConnection>.Success(persisted);
    }

    private ValueTask<HealingAuditEvent> AppendAuditAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid aggregateId,
        string eventType,
        HealingAuthorization authorization,
        CreateHealingAuthorityProfile request,
        CancellationToken cancellationToken) =>
        auditService.AppendAsync(new HealingAuditWrite(
            workspaceId, "healing-authority", aggregateId, eventType, HealingOwnershipReasonCodes.Succeeded,
            "workspace-account", authorization.ActorId, Guid.NewGuid(), null, "1", null, null,
            new Dictionary<string, string?>
            {
                ["repositoryOwner"] = request.RepositoryOwner,
                ["repositoryName"] = request.RepositoryName,
                ["status"] = "pending-validation"
            }), cancellationToken);

    private static bool IsValid(CreateHealingAuthorityProfile request) =>
        !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 120 &&
        !string.IsNullOrWhiteSpace(request.InstallationId) && SafeIdentifier.IsMatch(request.InstallationId.Trim()) &&
        !string.IsNullOrWhiteSpace(request.RepositoryOwner) && SafeIdentifier.IsMatch(request.RepositoryOwner.Trim()) &&
        !string.IsNullOrWhiteSpace(request.RepositoryName) && SafeIdentifier.IsMatch(request.RepositoryName.Trim()) &&
        request.CredentialReferenceId != Guid.Empty &&
        request.AllowedRoots is { Count: > 0 and <= 32 } && request.AllowedRoots.All(IsSafePath) &&
        request.ForbiddenRoots is { Count: <= 64 } && request.ForbiddenRoots.All(IsSafePath) &&
        request.MaxFiles is > 0 and <= 100 && request.MaxChangedLines is > 0 and <= 10_000 &&
        request.MaxPatchBytes is > 0 and <= 10_000_000 &&
        request.MinimumInferenceConfidence is >= 0 and <= 1 &&
        (!request.AllowHighConfidenceInference || request.MinimumInferenceConfidence > 0) &&
        request.RequiredChecks is { Count: <= 64 } && request.RequiredChecks.All(IsSafeIdentifier) &&
        request.ForbiddenChangeCategories is { Count: <= 64 } && request.ForbiddenChangeCategories.All(IsSafeIdentifier) &&
        (string.IsNullOrWhiteSpace(request.IndependentVerifier) || IsSafeIdentifier(request.IndependentVerifier));

    private static bool IsSafeIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) && SafeIdentifier.IsMatch(value.Trim());
    private static bool IsSafePath(string? value) => !string.IsNullOrWhiteSpace(value) && SafePath.IsMatch(value.Trim()) && !value.Contains("\\", StringComparison.Ordinal);

    private static string PolicyHash(object value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType());
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()}";
    }
}
