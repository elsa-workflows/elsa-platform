using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.ComponentManifest;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Security;
using ComponentManifestEntity = Elsa.Platform.Healing.Core.ComponentManifest;

namespace Elsa.Platform.Healing.Core.Manifests;

public enum ManifestTrustMethod
{
    PlatformManagedBuildAttestation,
    AuthorizedExternalDelivery,
    WorkspaceOwnerVerification
}

public sealed record ComponentManifestAttestationRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid RevisionId,
    string SourceRevision,
    string? RepositoryUrl,
    string? BuildId,
    DateTimeOffset CreatedAt,
    string ManifestDigest,
    string CanonicalJson);

public sealed record ComponentManifestAttestationEvidence(
    string ExpectedManifestDigest,
    string ExpectedBuildId);

public sealed record ComponentManifestAttestationDecision(
    bool Succeeded,
    ManifestTrustMethod Method,
    string ActorType,
    string ActorId,
    string ReasonCode);

/// <summary>
/// Trusted infrastructure boundary for build or delivery attestations. This service must never be
/// implemented from request data supplied by a workspace owner.
/// </summary>
public interface IComponentManifestAttestationAuthority
{
    ValueTask<ComponentManifestAttestationDecision> VerifyAsync(
        ComponentManifestAttestationRequest request,
        ComponentManifestAttestationEvidence evidence,
        CancellationToken cancellationToken = default);
}

public sealed record ManifestRegistrationResult(
    bool Succeeded,
    string ReasonCode,
    ComponentManifestEntity? Manifest = null,
    bool IsReplay = false);

public sealed class ComponentManifestService(
    IHealingOwnershipStore store,
    HealingAuditService auditService,
    TimeProvider? timeProvider = null,
    IComponentManifestAttestationAuthority? attestationAuthority = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<ManifestRegistrationResult> RegisterAsync(
        ComponentManifestEntity manifest,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(manifest, null, null, authorization, cancellationToken);

    public ValueTask<ManifestRegistrationResult> RegisterAsync(
        ComponentManifestEntity manifest,
        string idempotencyKey,
        string payloadHash,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(manifest, idempotencyKey, payloadHash, authorization, cancellationToken);

    private async ValueTask<ManifestRegistrationResult> RegisterCoreAsync(
        ComponentManifestEntity manifest,
        string? idempotencyKey,
        string? payloadHash,
        HealingAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var authorizationFailure = HealingOwnershipAuthorization.ConfigurationFailure(
            authorization, manifest.WorkspaceId, manifest.ApplicationId);
        if (authorizationFailure is not null)
            return new ManifestRegistrationResult(false, authorizationFailure);
        if (!PrepareAndValidate(manifest))
            return new ManifestRegistrationResult(false, HealingOwnershipReasonCodes.InvalidManifest);

        manifest.TrustState = ComponentManifestTrustState.Unverified;
        if (manifest.CreatedAt == default)
            manifest.CreatedAt = _timeProvider.GetUtcNow();
        var result = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var persisted = idempotencyKey is null
                ? await RegisterWithoutIdempotencyKeyAsync(store, manifest, transactionCancellationToken)
                : await store.RegisterManifestAsync(manifest, idempotencyKey, payloadHash!, transactionCancellationToken);
            if (persisted.FailureReasonCode is null)
                await AuditAsync(
                    persisted.Value,
                    "manifest-registered",
                    persisted.IsReplay ? "replayed" : "unverified",
                    HealingActorTypes.Human,
                    authorization.ActorId,
                    transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        if (result.FailureReasonCode is not null)
            return new ManifestRegistrationResult(false, result.FailureReasonCode, result.Value, true);
        return new ManifestRegistrationResult(true, HealingOwnershipReasonCodes.Succeeded, result.Value, result.IsReplay);
    }

    private static async ValueTask<ManifestRegistrationWriteResult> RegisterWithoutIdempotencyKeyAsync(
        IHealingOwnershipStore store,
        ComponentManifestEntity manifest,
        CancellationToken cancellationToken)
    {
        var persisted = await store.AddManifestAsync(manifest, cancellationToken);
        return new ManifestRegistrationWriteResult(
            persisted.Value,
            persisted.IsReplay,
            persisted.IsConsistentReplay ? null : HealingOwnershipReasonCodes.ImmutableRevisionConflict);
    }

    public ValueTask<HealingOperationResult<ComponentManifestEntity>> VerifyByOwnerAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        return authorizationFailure is not null
            ? ValueTask.FromResult(HealingOperationResult<ComponentManifestEntity>.Denied(authorizationFailure))
            : TransitionAsync(
                workspaceId, applicationId, manifestId, ComponentManifestTrustState.Verified,
                ManifestTrustMethod.WorkspaceOwnerVerification, HealingActorTypes.Human, authorization.ActorId,
                cancellationToken);
    }

    /// <summary>
    /// Compatibility entry point. Owner requests may only select owner verification; stronger
    /// methods require <see cref="VerifyAttestedAsync"/> and its trusted authority.
    /// </summary>
    public ValueTask<HealingOperationResult<ComponentManifestEntity>> VerifyAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        ManifestTrustMethod method,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (method == ManifestTrustMethod.WorkspaceOwnerVerification)
            return VerifyByOwnerAsync(workspaceId, applicationId, manifestId, authorization, cancellationToken);
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        return ValueTask.FromResult(HealingOperationResult<ComponentManifestEntity>.Denied(
            authorizationFailure ?? HealingOwnershipReasonCodes.TrustedAttestationRequired));
    }

    public async ValueTask<HealingOperationResult<ComponentManifestEntity>> VerifyAttestedAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        ComponentManifestAttestationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (attestationAuthority is null)
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.TrustedAttestationRequired);
        var manifest = await store.GetManifestAsync(workspaceId, applicationId, manifestId, cancellationToken);
        if (manifest is null)
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.NotFound);

        HealingComponentManifest document;
        try
        {
            document = ComponentManifestSerializer.Deserialize(manifest.CanonicalJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ComponentManifestValidationException or ArgumentException)
        {
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.InvalidManifest);
        }
        if (!string.Equals(manifest.SchemaVersion, document.SchemaVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.SourceRevision, document.Revision.SourceRevision, StringComparison.Ordinal) ||
            !string.Equals(manifest.BuildId, document.Revision.BuildId, StringComparison.Ordinal) ||
            manifest.CreatedAt != document.Revision.CreatedAt ||
            !string.Equals(manifest.ManifestDigest, document.ManifestDigest, StringComparison.Ordinal))
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.InvalidManifest);
        var decision = await attestationAuthority.VerifyAsync(
            new ComponentManifestAttestationRequest(
                workspaceId,
                applicationId,
                manifest.RevisionId,
                manifest.SourceRevision,
                document.Revision.RepositoryUrl,
                manifest.BuildId,
                manifest.CreatedAt,
                manifest.ManifestDigest,
                manifest.CanonicalJson),
            evidence,
            cancellationToken);
        if (!decision.Succeeded)
            return HealingOperationResult<ComponentManifestEntity>.Denied(
                string.IsNullOrWhiteSpace(decision.ReasonCode) ? HealingOwnershipReasonCodes.AttestationRejected : decision.ReasonCode);
        if (!IsValidAttestationDecision(decision))
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.AttestationRejected);

        return await TransitionAsync(
            workspaceId, applicationId, manifestId, ComponentManifestTrustState.Verified,
            decision.Method, decision.ActorType, decision.ActorId, cancellationToken);
    }

    public ValueTask<HealingOperationResult<ComponentManifestEntity>> RevokeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default) =>
        RevokeByOwnerAsync(workspaceId, applicationId, manifestId, authorization, cancellationToken);

    private ValueTask<HealingOperationResult<ComponentManifestEntity>> RevokeByOwnerAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        return authorizationFailure is not null
            ? ValueTask.FromResult(HealingOperationResult<ComponentManifestEntity>.Denied(authorizationFailure))
            : TransitionAsync(
                workspaceId, applicationId, manifestId, ComponentManifestTrustState.Revoked,
                ManifestTrustMethod.WorkspaceOwnerVerification, HealingActorTypes.Human, authorization.ActorId,
                cancellationToken);
    }

    public async ValueTask<HealingOperationResult<ComponentManifestEntity>> GetAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<ComponentManifestEntity>.Denied(authorizationFailure);
        var manifest = await store.GetManifestAsync(workspaceId, applicationId, manifestId, cancellationToken);
        return manifest is null
            ? HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.NotFound)
            : HealingOperationResult<ComponentManifestEntity>.Success(manifest);
    }

    public async ValueTask<IReadOnlyList<ComponentManifestEntity>> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        return authorizationFailure is null
            ? await store.ListManifestsAsync(workspaceId, applicationId, trustedOnly: false, cancellationToken)
            : [];
    }

    private async ValueTask<HealingOperationResult<ComponentManifestEntity>> TransitionAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        ComponentManifestTrustState target,
        ManifestTrustMethod method,
        string actorType,
        string actorId,
        CancellationToken cancellationToken)
    {
        var manifest = await store.GetManifestAsync(workspaceId, applicationId, manifestId, cancellationToken);
        if (manifest is null)
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (manifest.TrustState == target &&
            (target != ComponentManifestTrustState.Verified ||
             IsAutomationAuthoritative(manifest) ||
             method == ManifestTrustMethod.WorkspaceOwnerVerification))
            return HealingOperationResult<ComponentManifestEntity>.Success(manifest);
        var strengthensOwnerVerification =
            target == ComponentManifestTrustState.Verified &&
            manifest.TrustState == ComponentManifestTrustState.Verified &&
            !IsAutomationAuthoritative(manifest) &&
            IsAutomationAuthoritative(method);
        if ((target == ComponentManifestTrustState.Verified &&
             manifest.TrustState != ComponentManifestTrustState.Unverified &&
             !strengthensOwnerVerification) ||
            (target == ComponentManifestTrustState.Revoked && manifest.TrustState == ComponentManifestTrustState.Rejected))
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.InvalidTrustTransition);

        var changed = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var transitioned = await store.TransitionManifestTrustAsync(
                workspaceId,
                applicationId,
                manifestId,
                manifest.TrustState,
                target,
                actorId,
                TrustMethodCode(method),
                _timeProvider.GetUtcNow(),
                transactionCancellationToken);
            if (!transitioned)
                return false;
            await AuditAsync(
                manifest,
                target == ComponentManifestTrustState.Revoked ? "manifest-revoked" : "manifest-verified",
                target.ToString().ToLowerInvariant(), actorType, actorId, transactionCancellationToken);
            return true;
        }, cancellationToken);
        if (!changed)
            return HealingOperationResult<ComponentManifestEntity>.Denied(HealingOwnershipReasonCodes.InvalidTrustTransition);

        manifest = await store.GetManifestAsync(workspaceId, applicationId, manifestId, cancellationToken);
        return HealingOperationResult<ComponentManifestEntity>.Success(manifest!);
    }

    private static bool PrepareAndValidate(ComponentManifestEntity manifest)
    {
        if (manifest.Id == Guid.Empty || manifest.WorkspaceId == Guid.Empty || manifest.ApplicationId == Guid.Empty ||
            manifest.RevisionId == Guid.Empty || string.IsNullOrWhiteSpace(manifest.CanonicalJson))
            return false;

        HealingComponentManifest document;
        try
        {
            document = ComponentManifestSerializer.Deserialize(manifest.CanonicalJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ComponentManifestValidationException or ArgumentException)
        {
            return false;
        }
        if (document.Components.Count == 0)
            return false;

        var entriesByKey = new Dictionary<string, ComponentManifestEntry>(StringComparer.Ordinal);
        foreach (var component in document.Components)
        {
            var entryId = Guid.NewGuid();
            var entry = new ComponentManifestEntry
            {
                Id = entryId,
                ManifestId = manifest.Id,
                WorkspaceId = manifest.WorkspaceId,
                ApplicationId = manifest.ApplicationId,
                ComponentKey = component.Key,
                Kind = MapKind(component.Kind),
                KindName = component.Kind,
                Name = component.Name,
                Version = component.Version,
                PackageId = component.Kind == "package" ? component.Name : null,
                PackageVersion = component.Kind == "package" ? component.Version : null,
                ContentHash = component.ContentHash,
                RepositoryUrl = component.RepositoryUrl,
                RepositoryCommit = component.RepositoryCommit,
                IsDirectDependency = component.DirectDependency
            };
            foreach (var assembly in component.Assemblies)
            {
                if (!TryNormalizeRelativePath(assembly.RelativePath, out var normalizedPath))
                    return false;
                entry.Assemblies.Add(new ComponentManifestAssemblyArtifact
                {
                    Id = Guid.NewGuid(),
                    ManifestId = manifest.Id,
                    ComponentEntryId = entryId,
                    WorkspaceId = manifest.WorkspaceId,
                    ApplicationId = manifest.ApplicationId,
                    Name = assembly.Name,
                    Version = assembly.Version,
                    PublicKeyToken = assembly.PublicKeyToken,
                    RelativePath = normalizedPath,
                    ContentHash = assembly.ContentHash
                });
            }
            entriesByKey.Add(component.Key, entry);
        }

        manifest.SchemaVersion = document.SchemaVersion;
        manifest.SourceRevision = document.Revision.SourceRevision;
        manifest.BuildId = document.Revision.BuildId;
        manifest.ManifestDigest = document.ManifestDigest!;
        manifest.CanonicalJson = ComponentManifestSerializer.Serialize(document);
        manifest.CreatedAt = document.Revision.CreatedAt;
        manifest.Entries = document.Components.Select(component => entriesByKey[component.Key]).ToList();
        var dependencies = new List<ComponentDependency>();
        foreach (var component in document.Components)
        {
            var dependencyKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in component.Dependencies)
            {
                if (dependency == component.Key || !dependencyKeys.Add(dependency))
                    return false;
                dependencies.Add(new ComponentDependency
                {
                    Id = Guid.NewGuid(),
                    ManifestId = manifest.Id,
                    FromEntryId = entriesByKey[component.Key].Id,
                    ToEntryId = entriesByKey[dependency].Id
                });
            }
        }
        manifest.Dependencies = dependencies;
        return true;
    }

    private static ComponentKind MapKind(string kind) => kind switch
    {
        "application" => ComponentKind.Application,
        "package" => ComponentKind.Package,
        "assembly" => ComponentKind.Assembly,
        _ => ComponentKind.Unknown
    };

    private static bool TryNormalizeRelativePath(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var candidate = path.Replace('\\', '/').Trim();
        if (candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':' ||
            candidate.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            return false;
        normalized = string.Join('/', candidate.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 0;
    }

    private ValueTask<HealingAuditEvent> AuditAsync(
        ComponentManifestEntity manifest,
        string eventType,
        string status,
        string actorType,
        string actorId,
        CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, string?> { ["status"] = status };
        if (manifest.SourceRevision.Length is 40 or 64 && manifest.SourceRevision.All(char.IsAsciiHexDigit))
            details["revision"] = manifest.SourceRevision.ToLowerInvariant();
        return auditService.AppendAsync(new HealingAuditWrite(
            manifest.WorkspaceId,
            "component-manifest",
            manifest.Id,
            eventType,
            HealingOwnershipReasonCodes.Succeeded,
            actorType,
            actorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            details), cancellationToken);
    }

    private static string TrustMethodCode(ManifestTrustMethod method) => method switch
    {
        ManifestTrustMethod.PlatformManagedBuildAttestation => "platform-managed-build-attestation",
        ManifestTrustMethod.AuthorizedExternalDelivery => "authorized-external-delivery",
        ManifestTrustMethod.WorkspaceOwnerVerification => "workspace-owner-verification",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    public static bool IsAutomationAuthoritative(ComponentManifestEntity manifest) =>
        manifest.TrustState == ComponentManifestTrustState.Verified &&
        manifest.VerificationMethod is "platform-managed-build-attestation" or "authorized-external-delivery";

    private static bool IsAutomationAuthoritative(ManifestTrustMethod method) =>
        method is ManifestTrustMethod.PlatformManagedBuildAttestation or ManifestTrustMethod.AuthorizedExternalDelivery;

    private static bool IsValidAttestationDecision(ComponentManifestAttestationDecision decision) =>
        !string.IsNullOrWhiteSpace(decision.ActorId) &&
        (decision.Method == ManifestTrustMethod.PlatformManagedBuildAttestation && decision.ActorType == HealingActorTypes.Platform ||
         decision.Method == ManifestTrustMethod.AuthorizedExternalDelivery && decision.ActorType == HealingActorTypes.DeploymentSystem);
}
