using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Incidents;
using Elsa.Platform.Healing.Core.OpenTelemetry;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Core.Security;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using ComponentManifestModel = Elsa.Platform.Healing.Core.ComponentManifest;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed record HealingStoreWriteResult<T>(T Value, bool IsReplay);

public sealed record HealingStoreLease<T>(T Value, string LeaseToken);

public sealed class HealingIdempotencyConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// Healing-owned durable store. Idempotency, atomic leases, and append-only audit behavior are kept behind
/// this interface so callers do not need provider-specific persistence knowledge.
/// </summary>
public sealed class HealingStore(HealingDbContext dbContext) : IHealingAuditStore, IHealingOwnershipStore, IHealingAdministrationStore, IHealingIncidentStore, IHealingSignalInboxStore, IHealingTelemetrySourceStore, IProviderOperationStore, IHealingEvidenceStore, IRepairOrchestrationStore
{
    public ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default) =>
        HealingPersistenceTransaction.ExecuteAsync(dbContext, operation, cancellationToken);

    public async ValueTask<HealingWorkspaceConfiguration> UpsertWorkspaceConfigurationAsync(
        HealingWorkspaceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var existing = await dbContext.HealingWorkspaceConfigurations
            .SingleOrDefaultAsync(x => x.WorkspaceId == configuration.WorkspaceId, cancellationToken);
        if (existing is null)
        {
            configuration.Version = NewVersion();
            dbContext.HealingWorkspaceConfigurations.Add(configuration);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return configuration;
            }
            catch (DbUpdateException exception)
            {
                dbContext.Entry(configuration).State = EntityState.Detached;
                existing = await dbContext.HealingWorkspaceConfigurations.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.WorkspaceId == configuration.WorkspaceId, cancellationToken);
                if (existing is null)
                    throw;
                if (existing.WorkspaceKillSwitch == configuration.WorkspaceKillSwitch)
                    return existing;
                throw ConcurrentCreate("Healing workspace configuration", exception);
            }
        }

        if (configuration.Version.Length == 0)
        {
            if (existing.WorkspaceKillSwitch == configuration.WorkspaceKillSwitch)
                return existing;
            throw ConcurrentCreate("Healing workspace configuration");
        }
        EnsureExpectedVersion(configuration.Version, existing.Version, "Healing workspace configuration");
        existing.WorkspaceKillSwitch = configuration.WorkspaceKillSwitch;
        existing.UpdatedAt = configuration.UpdatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public ValueTask<HealingWorkspaceConfiguration?> GetWorkspaceConfigurationAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.HealingWorkspaceConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken));

    public async ValueTask<HealingTelemetrySource> AddTelemetrySourceAsync(
        HealingTelemetrySource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateTelemetrySource(source);
        if (source.Id == Guid.Empty)
            source.Id = Guid.NewGuid();
        dbContext.HealingTelemetrySources.Add(source);
        await dbContext.SaveChangesAsync(cancellationToken);
        return source;
    }

    public async ValueTask<IReadOnlyList<HealingTelemetrySource>> ListTelemetrySourcesAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.HealingTelemetrySources.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == applicationId &&
                        x.EnvironmentId == environmentId)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public ValueTask<HealingTelemetrySource?> GetTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.HealingTelemetrySources.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId &&
                 x.ApplicationId == applicationId &&
                 x.EnvironmentId == environmentId &&
                 x.Id == sourceId,
            cancellationToken));

    public ValueTask<HealingTelemetrySource?> GetActiveTelemetrySourceForAuthenticationAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.HealingTelemetrySources.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == sourceId && x.Status == HealingTelemetrySourceStatus.Active,
            cancellationToken));

    public async ValueTask<HealingTelemetrySource?> RotateTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        byte[] expectedVersion,
        byte[] credentialSalt,
        byte[] credentialHash,
        DateTimeOffset rotatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateCredential(credentialSalt, credentialHash);
        var source = await dbContext.HealingTelemetrySources.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId &&
                 x.ApplicationId == applicationId &&
                 x.EnvironmentId == environmentId &&
                 x.Id == sourceId &&
                 x.Status == HealingTelemetrySourceStatus.Active,
            cancellationToken);
        if (source is null)
            return null;

        EnsureExpectedVersion(expectedVersion, source.Version, "Healing telemetry source");
        source.CredentialSalt = credentialSalt;
        source.CredentialHash = credentialHash;
        source.CredentialVersion++;
        source.RotatedAt = rotatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return source;
    }

    public async ValueTask<HealingTelemetrySource?> RevokeTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        byte[] expectedVersion,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.HealingTelemetrySources.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId &&
                 x.ApplicationId == applicationId &&
                 x.EnvironmentId == environmentId &&
                 x.Id == sourceId,
            cancellationToken);
        if (source is null)
            return null;
        if (source.Status == HealingTelemetrySourceStatus.Revoked)
            return source;

        EnsureExpectedVersion(expectedVersion, source.Version, "Healing telemetry source");
        source.Status = HealingTelemetrySourceStatus.Revoked;
        source.RevokedAt = revokedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return source;
    }

    public async ValueTask<HealingConfiguration> UpsertConfigurationAsync(
        HealingConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateEnvironmentOverrides(configuration);
        var existing = await dbContext.HealingConfigurations
            .Include(x => x.Environments)
            .SingleOrDefaultAsync(x => x.WorkspaceId == configuration.WorkspaceId && x.ApplicationId == configuration.ApplicationId, cancellationToken);
        if (existing is null)
        {
            foreach (var environment in configuration.Environments)
            {
                if (environment.Id == Guid.Empty)
                    environment.Id = Guid.NewGuid();
                environment.HealingConfigurationId = configuration.Id;
            }
            configuration.Version = NewVersion();
            dbContext.HealingConfigurations.Add(configuration);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return configuration;
            }
            catch (DbUpdateException exception)
            {
                DetachConfigurationGraph(configuration);
                existing = await dbContext.HealingConfigurations.AsNoTracking()
                    .Include(x => x.Environments)
                    .SingleOrDefaultAsync(x => x.WorkspaceId == configuration.WorkspaceId && x.ApplicationId == configuration.ApplicationId, cancellationToken);
                if (existing is null)
                    throw;
                if (ConfigurationsEquivalent(existing, configuration))
                    return existing;
                throw ConcurrentCreate("Healing configuration", exception);
            }
        }

        if (configuration.Version.Length == 0)
        {
            if (ConfigurationsEquivalent(existing, configuration))
                return existing;
            throw ConcurrentCreate("Healing configuration");
        }
        EnsureExpectedVersion(configuration.Version, existing.Version, "Healing configuration");

        existing.DiscoveryEnabled = configuration.DiscoveryEnabled;
        existing.RepairEnabled = configuration.RepairEnabled;
        existing.AutomaticMergeEnabled = configuration.AutomaticMergeEnabled;
        existing.SignalProfileVersion = configuration.SignalProfileVersion;
        existing.DefaultAttemptLimit = configuration.DefaultAttemptLimit;
        existing.VerificationWindow = configuration.VerificationWindow;
        existing.TimeBudget = configuration.TimeBudget;
        existing.ConcurrencyBudget = configuration.ConcurrencyBudget;
        existing.InferenceBudget = configuration.InferenceBudget;
        existing.RepositoryRunBudget = configuration.RepositoryRunBudget;
        existing.ApplicationKillSwitch = configuration.ApplicationKillSwitch;
        existing.ClassificationPolicyJson = configuration.ClassificationPolicyJson;
        existing.UpdatedAt = configuration.UpdatedAt;
        existing.Version = NewVersion();
        ReconcileEnvironmentOverrides(existing, configuration.Environments);
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public ValueTask<HealingConfiguration?> GetConfigurationAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.HealingConfigurations.AsNoTracking()
            .Include(x => x.Environments)
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId, cancellationToken));

    public ValueTask<HealingConfiguration> SaveConfigurationAsync(
        HealingConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        UpsertConfigurationAsync(configuration, cancellationToken);

    public async ValueTask<HealingStoreWriteResult<ComponentManifestModel>> AppendManifestAsync(
        ComponentManifestModel manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var existing = await dbContext.ComponentManifests.AsNoTracking()
            .Include(x => x.Entries).ThenInclude(x => x.Assemblies)
            .Include(x => x.Dependencies)
            .SingleOrDefaultAsync(x => x.WorkspaceId == manifest.WorkspaceId &&
                                       x.ApplicationId == manifest.ApplicationId &&
                                       x.RevisionId == manifest.RevisionId, cancellationToken);
        if (existing is not null)
            return MatchReplay(existing, manifest.ManifestDigest, existing.ManifestDigest, "Component manifest revision");

        dbContext.ComponentManifests.Add(manifest);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<ComponentManifestModel>(manifest, false);
        }
        catch (DbUpdateException)
        {
            DetachManifestGraph(manifest);
            existing = await dbContext.ComponentManifests.AsNoTracking()
                .Include(x => x.Entries).ThenInclude(x => x.Assemblies)
                .Include(x => x.Dependencies)
                .SingleOrDefaultAsync(x => x.WorkspaceId == manifest.WorkspaceId &&
                                           x.ApplicationId == manifest.ApplicationId &&
                                           x.RevisionId == manifest.RevisionId, cancellationToken);
            if (existing is null)
                throw;
            return MatchReplay(existing, manifest.ManifestDigest, existing.ManifestDigest, "Component manifest revision");
        }
    }

    public async ValueTask<OwnershipWriteResult<ComponentManifestModel>> AddManifestAsync(
        ComponentManifestModel manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await AppendManifestAsync(manifest, cancellationToken);
            return new OwnershipWriteResult<ComponentManifestModel>(result.Value, result.IsReplay);
        }
        catch (HealingIdempotencyConflictException)
        {
            var existing = await dbContext.ComponentManifests.AsNoTracking()
                .Include(x => x.Entries).ThenInclude(x => x.Assemblies)
                .Include(x => x.Dependencies)
                .SingleAsync(x => x.WorkspaceId == manifest.WorkspaceId &&
                                  x.ApplicationId == manifest.ApplicationId &&
                                  x.RevisionId == manifest.RevisionId, cancellationToken);
            return new OwnershipWriteResult<ComponentManifestModel>(existing, true, false);
        }
    }

    public async ValueTask<ManifestRegistrationWriteResult> RegisterManifestAsync(
        ComponentManifestModel manifest,
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        var registration = await dbContext.ComponentManifestRegistrations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == manifest.WorkspaceId &&
                                       x.ApplicationId == manifest.ApplicationId &&
                                       x.RevisionId == manifest.RevisionId &&
                                       x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (registration is not null)
        {
            var registeredManifest = await GetManifestAsync(
                manifest.WorkspaceId, manifest.ApplicationId, registration.ManifestId, cancellationToken)
                ?? throw new InvalidOperationException("A manifest registration references a missing manifest.");
            return string.Equals(registration.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? new ManifestRegistrationWriteResult(registeredManifest, true)
                : new ManifestRegistrationWriteResult(registeredManifest, true, HealingOwnershipReasonCodes.IdempotencyConflict);
        }

        var persisted = await AddManifestAsync(manifest, cancellationToken);
        if (!persisted.IsConsistentReplay)
            return new ManifestRegistrationWriteResult(
                persisted.Value, true, HealingOwnershipReasonCodes.ImmutableRevisionConflict);

        dbContext.ComponentManifestRegistrations.Add(new ComponentManifestRegistration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = manifest.WorkspaceId,
            ApplicationId = manifest.ApplicationId,
            RevisionId = manifest.RevisionId,
            IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash,
            ManifestId = persisted.Value.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ManifestRegistrationWriteResult(persisted.Value, persisted.IsReplay);
    }

    public ValueTask<ComponentManifestModel?> GetManifestAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.ComponentManifests.AsNoTracking()
            .Include(x => x.Entries).ThenInclude(x => x.Assemblies)
            .Include(x => x.Dependencies)
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
                                       x.ApplicationId == applicationId &&
                                       x.Id == manifestId, cancellationToken));

    public async ValueTask<IReadOnlyList<ComponentManifestModel>> ListManifestsAsync(
        Guid workspaceId,
        Guid applicationId,
        bool trustedOnly,
        CancellationToken cancellationToken = default)
    {
        var manifests = dbContext.ComponentManifests.AsNoTracking()
            .Include(x => x.Entries).ThenInclude(x => x.Assemblies)
            .Include(x => x.Dependencies)
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId);
        if (trustedOnly)
            manifests = manifests.Where(x => x.TrustState == ComponentManifestTrustState.Verified);
        return await manifests.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async ValueTask<bool> TransitionManifestTrustAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        ComponentManifestTrustState expected,
        ComponentManifestTrustState target,
        string actorId,
        string method,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor identity is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("Trust method is required.", nameof(method));

        var query = dbContext.ComponentManifests.Where(x =>
            x.WorkspaceId == workspaceId && x.ApplicationId == applicationId &&
            x.Id == manifestId && x.TrustState == expected);
        var changed = target == ComponentManifestTrustState.Verified
            ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.TrustState, target)
                .SetProperty(x => x.VerifiedBy, actorId)
                .SetProperty(x => x.VerifiedAt, now)
                .SetProperty(x => x.VerificationMethod, method), cancellationToken)
            : await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.TrustState, target), cancellationToken);
        return changed == 1;
    }

    public async ValueTask<IReadOnlyList<SourceOwnershipBinding>> ListBindingsAsync(
        Guid workspaceId,
        Guid applicationId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var bindings = dbContext.SourceOwnershipBindings.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId);
        if (activeOnly)
            bindings = bindings.Where(x => x.Status == SourceOwnershipBindingStatus.Active);
        return await bindings.OrderByDescending(x => x.Priority).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public ValueTask<SourceOwnershipBinding?> GetBindingAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == bindingId,
            cancellationToken));

    public ValueTask<ProviderConnection?> GetProviderConnectionAsync(
        Guid workspaceId,
        Guid providerConnectionId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.ProviderConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == providerConnectionId, cancellationToken));

    public async ValueTask<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await dbContext.ProviderConnections.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.RepositoryOwner).ThenBy(x => x.RepositoryName).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<PathPolicy>> ListPathPoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.PathPolicies.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<EvidencePolicy>> ListEvidencePoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.EvidencePolicies.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<MergePolicy>> ListMergePoliciesAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.MergePolicies.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<ProviderConnection> SaveProviderConnectionAsync(
        ProviderConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var existing = await dbContext.ProviderConnections.SingleOrDefaultAsync(
            x => x.WorkspaceId == connection.WorkspaceId && x.Id == connection.Id,
            cancellationToken);
        if (existing is not null)
        {
            EnsureExpectedVersion(connection.Version, existing.Version, "Provider connection");
            var initialValidation = existing.Status == ProviderConnectionStatus.PendingValidation &&
                                    connection.Status == ProviderConnectionStatus.Active;
            var revalidation = existing.Status == ProviderConnectionStatus.Suspended &&
                               connection.Status == ProviderConnectionStatus.Active;
            if (!string.Equals(existing.Provider, connection.Provider, StringComparison.Ordinal) ||
                !string.Equals(existing.InstallationId, connection.InstallationId, StringComparison.Ordinal) ||
                !initialValidation && !string.Equals(existing.RepositoryProviderId, connection.RepositoryProviderId, StringComparison.Ordinal) ||
                !string.Equals(existing.RepositoryOwner, connection.RepositoryOwner, StringComparison.Ordinal) ||
                !string.Equals(existing.RepositoryName, connection.RepositoryName, StringComparison.Ordinal) ||
                !string.Equals(existing.CredentialReference, connection.CredentialReference, StringComparison.Ordinal))
                throw new HealingAdministrationConflictException("Provider connection authority is immutable.");
            if (initialValidation || revalidation)
            {
                var repositoryConflict = await dbContext.ProviderConnections.AnyAsync(
                    x => x.WorkspaceId == connection.WorkspaceId &&
                         x.Provider == connection.Provider &&
                         x.RepositoryProviderId == connection.RepositoryProviderId &&
                         x.Id != connection.Id &&
                         x.Status != ProviderConnectionStatus.Revoked,
                    cancellationToken);
                if (repositoryConflict)
                    throw new HealingAdministrationConflictException("A provider connection already authorizes this repository.");
                if (initialValidation)
                    existing.RepositoryProviderId = connection.RepositoryProviderId;
            }
            existing.Status = connection.Status;
            existing.UpdatedAt = connection.UpdatedAt;
            existing.Version = NewVersion();
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var existingRepository = await dbContext.ProviderConnections.SingleOrDefaultAsync(
            x => x.WorkspaceId == connection.WorkspaceId &&
                 x.Provider == connection.Provider &&
                 x.RepositoryProviderId == connection.RepositoryProviderId &&
                 x.Status != ProviderConnectionStatus.Revoked,
            cancellationToken);
        if (existingRepository is not null)
            throw new HealingAdministrationConflictException("A provider connection already authorizes this repository.");

        dbContext.ProviderConnections.Add(connection);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new HealingAdministrationConflictException($"Provider connection could not be saved: {exception.GetType().Name}.");
        }
        return connection;
    }

    public async ValueTask SavePoliciesAsync(
        PathPolicy pathPolicy,
        EvidencePolicy evidencePolicy,
        MergePolicy mergePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);
        ArgumentNullException.ThrowIfNull(evidencePolicy);
        ArgumentNullException.ThrowIfNull(mergePolicy);
        if (pathPolicy.WorkspaceId != evidencePolicy.WorkspaceId || pathPolicy.WorkspaceId != mergePolicy.WorkspaceId ||
            pathPolicy.ApplicationId != evidencePolicy.ApplicationId || pathPolicy.ApplicationId != mergePolicy.ApplicationId)
            throw new InvalidOperationException("Policy bundle scope must match.");
        var duplicateName = await dbContext.PathPolicies.AnyAsync(
                                x => x.WorkspaceId == pathPolicy.WorkspaceId && x.ApplicationId == pathPolicy.ApplicationId &&
                                     x.Name == pathPolicy.Name && x.PolicyVersion == pathPolicy.PolicyVersion, cancellationToken) ||
                            await dbContext.EvidencePolicies.AnyAsync(
                                x => x.WorkspaceId == evidencePolicy.WorkspaceId && x.ApplicationId == evidencePolicy.ApplicationId &&
                                     x.Name == evidencePolicy.Name && x.PolicyVersion == evidencePolicy.PolicyVersion, cancellationToken) ||
                            await dbContext.MergePolicies.AnyAsync(
                                x => x.WorkspaceId == mergePolicy.WorkspaceId && x.ApplicationId == mergePolicy.ApplicationId &&
                                     x.Name == mergePolicy.Name && x.PolicyVersion == mergePolicy.PolicyVersion, cancellationToken);
        if (duplicateName)
            throw new HealingAdministrationConflictException("A policy profile with this name and version already exists.");
        dbContext.PathPolicies.Add(pathPolicy);
        dbContext.EvidencePolicies.Add(evidencePolicy);
        dbContext.MergePolicies.Add(mergePolicy);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new HealingAdministrationConflictException($"Policy profile could not be saved: {exception.GetType().Name}.");
        }
    }

    public async ValueTask<bool> PoliciesAreTrustedAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid pathPolicyId,
        Guid evidencePolicyId,
        Guid mergePolicyId,
        CancellationToken cancellationToken = default) =>
        await dbContext.PathPolicies.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == pathPolicyId &&
                 x.PolicyVersion != "" && x.PolicyHash != "", cancellationToken) &&
        await dbContext.EvidencePolicies.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == evidencePolicyId &&
                 x.PolicyVersion != "" && x.PolicyHash != "", cancellationToken) &&
        await dbContext.MergePolicies.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == mergePolicyId &&
                 x.PolicyVersion != "" && x.PolicyHash != "", cancellationToken);

    public async ValueTask<SourceOwnershipBinding> SaveBindingAsync(
        SourceOwnershipBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var existing = await dbContext.SourceOwnershipBindings.SingleOrDefaultAsync(
            x => x.WorkspaceId == binding.WorkspaceId &&
                 x.ApplicationId == binding.ApplicationId &&
                 x.Id == binding.Id, cancellationToken);
        if (existing is null)
        {
            binding.Version = NewVersion();
            dbContext.SourceOwnershipBindings.Add(binding);
            await dbContext.SaveChangesAsync(cancellationToken);
            return binding;
        }

        EnsureExpectedVersion(binding.Version, existing.Version, "Source ownership binding");
        existing.Name = binding.Name;
        existing.SelectorKind = binding.SelectorKind;
        existing.SelectorPattern = binding.SelectorPattern;
        existing.Priority = binding.Priority;
        existing.ProviderConnectionId = binding.ProviderConnectionId;
        existing.RepositoryProviderId = binding.RepositoryProviderId;
        existing.RepositoryOwner = binding.RepositoryOwner;
        existing.RepositoryName = binding.RepositoryName;
        existing.TargetBranch = binding.TargetBranch;
        existing.WorkflowIdentity = binding.WorkflowIdentity;
        existing.WorkflowReference = binding.WorkflowReference;
        existing.WorkflowRevision = binding.WorkflowRevision;
        existing.PathPolicyId = binding.PathPolicyId;
        existing.EvidencePolicyId = binding.EvidencePolicyId;
        existing.MergePolicyId = binding.MergePolicyId;
        existing.Status = binding.Status;
        existing.ApprovedBy = binding.ApprovedBy;
        existing.ApprovedAt = binding.ApprovedAt;
        existing.UpdatedAt = binding.UpdatedAt;
        existing.Version = NewVersion();
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async ValueTask<HealingStoreWriteResult<HealingIncident>> GetOrAddIncidentAsync(
        HealingIncident incident,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var existing = await ActiveIncidents()
            .SingleOrDefaultAsync(x => x.WorkspaceId == incident.WorkspaceId &&
                                       x.ApplicationId == incident.ApplicationId &&
                                       x.FingerprintVersion == incident.FingerprintVersion &&
                                       x.Fingerprint == incident.Fingerprint &&
                                       x.RepairRepositoryKey == incident.RepairRepositoryKey, cancellationToken);
        if (existing is not null)
            return new HealingStoreWriteResult<HealingIncident>(existing, true);

        incident.Version = NewVersion();
        dbContext.HealingIncidents.Add(incident);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<HealingIncident>(incident, false);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(incident).State = EntityState.Detached;
            existing = await ActiveIncidents()
                .SingleOrDefaultAsync(x => x.WorkspaceId == incident.WorkspaceId &&
                                           x.ApplicationId == incident.ApplicationId &&
                                           x.FingerprintVersion == incident.FingerprintVersion &&
                                           x.Fingerprint == incident.Fingerprint &&
                                           x.RepairRepositoryKey == incident.RepairRepositoryKey, cancellationToken);
            if (existing is null)
                throw;
            return new HealingStoreWriteResult<HealingIncident>(existing, true);
        }
    }

    public async ValueTask<HealingIncidentProjectionResult> ProjectOccurrenceAsync(
        HealingIncidentProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProjectionRequest(request);
        ConfigureSqliteWriterTimeout();

        const int maximumAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteInTransactionAsync(
                    transactionCancellationToken => ProjectOccurrenceCoreAsync(request, transactionCancellationToken),
                    cancellationToken);
            }
            catch (Exception exception) when (attempt < maximumAttempts && IsRetryableProjectionFailure(exception))
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(250, attempt * 15)), cancellationToken);
            }
        }
    }

    public async ValueTask<int> PromoteDueIncidentsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        ConfigureSqliteWriterTimeout();

        const int maximumAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteInTransactionAsync(async transactionCancellationToken =>
                {
                    var due = await dbContext.HealingIncidents
                        .Where(x => x.Status == HealingIncidentStatus.ThresholdPending &&
                                    x.SelectedBindingId != null &&
                                    x.ReadyAfter != null &&
                                    x.ReadyAfter <= now)
                        .OrderBy(x => x.ReadyAfter)
                        .ThenBy(x => x.Id)
                        .Take(batchSize)
                        .ToArrayAsync(transactionCancellationToken);
                    foreach (var incident in due)
                    {
                        incident.Status = HealingIncidentStatus.ReadyForRepair;
                        await EnsureWorkItemProjectionAsync(incident, transactionCancellationToken);
                    }
                    await dbContext.SaveChangesAsync(transactionCancellationToken);
                    return due.Length;
                }, cancellationToken);
            }
            catch (Exception exception) when (attempt < maximumAttempts && IsRetryableProjectionFailure(exception))
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(250, attempt * 15)), cancellationToken);
            }
        }
    }

    public async ValueTask<IReadOnlyList<HealingIncident>> ListIncidentsAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.HealingIncidents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId)
            .OrderByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public ValueTask<HealingIncident?> GetIncidentAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == incidentId,
            cancellationToken));

    private async ValueTask<HealingIncidentProjectionResult> ProjectOccurrenceCoreAsync(
        HealingIncidentProjectionRequest request,
        CancellationToken cancellationToken)
    {
        var replay = await dbContext.IncidentOccurrences.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == request.WorkspaceId &&
                 x.ApplicationId == request.ApplicationId &&
                 x.OccurrenceKey == request.OccurrenceKey,
            cancellationToken);
        if (replay is not null)
        {
            var replayIncident = await dbContext.HealingIncidents.AsNoTracking()
                .SingleAsync(x => x.Id == replay.IncidentId, cancellationToken);
            var replayEpisode = await dbContext.IncidentEpisodes.AsNoTracking()
                .SingleAsync(x => x.Id == replay.EpisodeId, cancellationToken);
            var replayImpact = await dbContext.EnvironmentImpacts.AsNoTracking()
                .SingleAsync(x => x.EpisodeId == replay.EpisodeId && x.EnvironmentId == replay.EnvironmentId, cancellationToken);
            return new HealingIncidentProjectionResult(
                replay,
                replayIncident,
                replayEpisode,
                replayImpact,
                IsReplay: true,
                IsRegression: replayEpisode.PreviousEpisodeId is not null);
        }

        var incident = await ActiveIncidents().SingleOrDefaultAsync(
            x => x.WorkspaceId == request.WorkspaceId &&
                 x.ApplicationId == request.ApplicationId &&
                 x.FingerprintVersion == request.FingerprintVersion &&
                 x.Fingerprint == request.Fingerprint &&
                 x.RepairRepositoryKey == request.RepairRepositoryKey,
            cancellationToken);
        IncidentEpisode episode;
        var isRegression = false;
        if (incident is null)
        {
            var predecessor = await dbContext.HealingIncidents.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId &&
                            x.ApplicationId == request.ApplicationId &&
                            x.FingerprintVersion == request.FingerprintVersion &&
                            x.Fingerprint == request.Fingerprint &&
                            x.RepairRepositoryKey == request.RepairRepositoryKey &&
                            (x.Status == HealingIncidentStatus.Failed ||
                             x.Status == HealingIncidentStatus.Healed ||
                             x.Status == HealingIncidentStatus.Superseded ||
                             x.Status == HealingIncidentStatus.Waived))
                .OrderByDescending(x => x.LastSeenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            Guid? previousEpisodeId = null;
            if (predecessor is not null)
            {
                previousEpisodeId = predecessor.ActiveEpisodeId ?? await dbContext.IncidentEpisodes.AsNoTracking()
                    .Where(x => x.IncidentId == predecessor.Id)
                    .OrderByDescending(x => x.OpenedAt)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                isRegression = previousEpisodeId is not null;
            }

            incident = new HealingIncident
            {
                Id = Guid.NewGuid(),
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                FingerprintVersion = request.FingerprintVersion,
                Fingerprint = request.Fingerprint,
                RepairRepositoryKey = request.RepairRepositoryKey,
                Status = InitialIncidentStatus(request),
                Severity = request.Severity,
                Classification = request.Classification,
                SelectedBindingId = request.SelectedBindingId,
                SelectedComponentEntryId = request.SelectedComponentEntryId,
                FirstSeenAt = request.OccurredAt,
                LastSeenAt = request.OccurredAt,
                OccurrenceCount = 0,
                ReadyAfter = request.OccurrenceThreshold == 1
                    ? request.AcceptedAt.Add(request.DebounceWindow)
                    : null,
                Version = NewVersion()
            };
            dbContext.HealingIncidents.Add(incident);
            await dbContext.SaveChangesAsync(cancellationToken);

            episode = new IncidentEpisode
            {
                Id = Guid.NewGuid(),
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                IncidentId = incident.Id,
                PreviousEpisodeId = previousEpisodeId,
                OpenedAt = request.OccurredAt,
                ProducingRevisionsJson = SerializeRevisions(request.RevisionId),
                Outcome = IncidentEpisodeOutcome.Active,
                RegressionReason = isRegression ? "fingerprint-recurred" : null,
                Version = NewVersion()
            };
            dbContext.IncidentEpisodes.Add(episode);
            await dbContext.SaveChangesAsync(cancellationToken);
            incident.ActiveEpisodeId = episode.Id;
        }
        else
        {
            incident = await dbContext.HealingIncidents.SingleAsync(x => x.Id == incident.Id, cancellationToken);
            episode = await dbContext.IncidentEpisodes.SingleAsync(
                x => x.Id == incident.ActiveEpisodeId,
                cancellationToken);
        }

        incident.FirstSeenAt = Earlier(incident.FirstSeenAt, request.OccurredAt);
        incident.LastSeenAt = Later(incident.LastSeenAt, request.OccurredAt);
        incident.OccurrenceCount++;
        if (request.Severity > incident.Severity)
            incident.Severity = request.Severity;
        episode.ProducingRevisionsJson = AddRevision(episode.ProducingRevisionsJson, request.RevisionId);

        var impact = await dbContext.EnvironmentImpacts.SingleOrDefaultAsync(
            x => x.EpisodeId == episode.Id && x.EnvironmentId == request.EnvironmentId,
            cancellationToken);
        if (impact is null)
        {
            impact = new EnvironmentImpact
            {
                Id = Guid.NewGuid(),
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                EpisodeId = episode.Id,
                EnvironmentId = request.EnvironmentId,
                FirstSeenAt = request.OccurredAt,
                LastSeenAt = request.OccurredAt,
                OccurrenceCount = 1,
                ProducingRevisionsJson = SerializeRevisions(request.RevisionId),
                VerificationStatus = VerificationOutcome.PendingDeployment,
                OccurrenceThreshold = request.OccurrenceThreshold,
                DebounceWindow = request.DebounceWindow,
                ClassificationPolicyVersion = request.ClassificationPolicyVersion,
                ClassificationPolicyHash = request.ClassificationPolicyHash,
                Version = NewVersion()
            };
            dbContext.EnvironmentImpacts.Add(impact);
        }
        else
        {
            impact.FirstSeenAt = Earlier(impact.FirstSeenAt, request.OccurredAt);
            impact.LastSeenAt = Later(impact.LastSeenAt, request.OccurredAt);
            impact.OccurrenceCount++;
            impact.ProducingRevisionsJson = AddRevision(impact.ProducingRevisionsJson, request.RevisionId);
        }

        if (impact.OccurrenceCount >= request.OccurrenceThreshold && incident.SelectedBindingId is not null)
        {
            var thresholdReachedAt = impact.ThresholdReachedAt ?? request.AcceptedAt;
            impact.ThresholdReachedAt ??= thresholdReachedAt;
            impact.ReadyAfter ??= thresholdReachedAt.Add(request.DebounceWindow);
            if (incident.ReadyAfter is null || impact.ReadyAfter < incident.ReadyAfter)
                incident.ReadyAfter = impact.ReadyAfter;
            if (incident.Status == HealingIncidentStatus.ThresholdPending)
            {
                var target = incident.ReadyAfter <= request.AcceptedAt
                    ? HealingIncidentStatus.ReadyForRepair
                    : HealingIncidentStatus.ThresholdPending;
                if (target != incident.Status)
                {
                    var transition = incident.TryTransitionTo(target);
                    if (!transition.Succeeded)
                        throw new InvalidOperationException($"Incident transition {transition.From} -> {transition.To} was rejected.");
                }
            }
        }

        var occurrence = new IncidentOccurrence
        {
            Id = Guid.NewGuid(),
            InboxItemId = request.InboxItemId,
            IncidentId = incident.Id,
            EpisodeId = episode.Id,
            WorkspaceId = request.WorkspaceId,
            ApplicationId = request.ApplicationId,
            EnvironmentId = request.EnvironmentId,
            RevisionId = request.RevisionId,
            OccurrenceKey = request.OccurrenceKey,
            OccurredAt = request.OccurredAt,
            AcceptedAt = request.AcceptedAt,
            Classification = request.Classification,
            Severity = request.Severity,
            ExceptionType = request.ExceptionType,
            OperationName = request.OperationName,
            NormalizedStackJson = request.NormalizedStackJson,
            TraceId = request.TraceId,
            SpanId = request.SpanId,
            RetryState = request.RetryState,
            FingerprintVersion = request.FingerprintVersion,
            Fingerprint = request.Fingerprint,
            EvidenceTier = request.EvidenceTier,
            EvidenceDigest = request.EvidenceDigest
        };
        dbContext.IncidentOccurrences.Add(occurrence);
        foreach (var attribution in request.Attributions)
        {
            dbContext.ComponentAttributions.Add(new ComponentAttribution
            {
                Id = Guid.NewGuid(),
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                OccurrenceId = occurrence.Id,
                ComponentEntryId = attribution.ComponentEntryId,
                BindingId = attribution.BindingId,
                Confidence = attribution.Confidence,
                Basis = attribution.Basis,
                Resolution = attribution.Resolution,
                ReasonCodesJson = JsonSerializer.Serialize(attribution.ReasonCodes)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (incident.Status == HealingIncidentStatus.ReadyForRepair)
        {
            await EnsureWorkItemProjectionAsync(incident, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (incident.WorkItemProjectionId is { } projectionId)
        {
            var projection = await dbContext.RepairWorkItemProjections.SingleOrDefaultAsync(
                x => x.Id == projectionId && x.ProjectionStatus != WorkItemProjectionStatus.Deleted,
                cancellationToken);
            if (projection is not null && projection.ProjectionStatus != WorkItemProjectionStatus.Pending)
            {
                projection.ProjectionStatus = WorkItemProjectionStatus.Pending;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return new HealingIncidentProjectionResult(occurrence, incident, episode, impact, false, isRegression);
    }

    private async ValueTask EnsureWorkItemProjectionAsync(
        HealingIncident incident,
        CancellationToken cancellationToken)
    {
        if (incident.WorkItemProjectionId is not null || incident.ActiveEpisodeId is null || incident.SelectedBindingId is null)
            return;
        var binding = await dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == incident.WorkspaceId &&
                 x.ApplicationId == incident.ApplicationId &&
                 x.Id == incident.SelectedBindingId &&
                 x.Status == SourceOwnershipBindingStatus.Active,
            cancellationToken);
        if (binding is null)
        {
            incident.Status = HealingIncidentStatus.ObservationOnly;
            return;
        }

        var projection = new RepairWorkItemProjection
        {
            Id = Guid.NewGuid(),
            WorkspaceId = incident.WorkspaceId,
            ApplicationId = incident.ApplicationId,
            IncidentId = incident.Id,
            EpisodeId = incident.ActiveEpisodeId.Value,
            ProviderConnectionId = binding.ProviderConnectionId,
            MachineSummaryHash = incident.Fingerprint,
            ProjectionStatus = WorkItemProjectionStatus.Pending
        };
        dbContext.RepairWorkItemProjections.Add(projection);
        await dbContext.SaveChangesAsync(cancellationToken);
        incident.WorkItemProjectionId = projection.Id;
    }

    public async ValueTask<HealingStoreWriteResult<RepairAttempt>> AppendAttemptAsync(
        RepairAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var existing = await dbContext.RepairAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EpisodeId == attempt.EpisodeId &&
                                       x.TargetRevision == attempt.TargetRevision &&
                                       x.AttemptNumber == attempt.AttemptNumber, cancellationToken);
        if (existing is not null)
        {
            if (existing.BindingId != attempt.BindingId || existing.EvidenceBundleId != attempt.EvidenceBundleId)
                throw new HealingIdempotencyConflictException("Repair attempt identity was reused with different authority or evidence.");
            return new HealingStoreWriteResult<RepairAttempt>(existing, true);
        }

        attempt.Version = NewVersion();
        dbContext.RepairAttempts.Add(attempt);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<RepairAttempt>(attempt, false);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(attempt).State = EntityState.Detached;
            existing = await dbContext.RepairAttempts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.EpisodeId == attempt.EpisodeId &&
                                           x.TargetRevision == attempt.TargetRevision &&
                                           x.AttemptNumber == attempt.AttemptNumber, cancellationToken);
            if (existing is null)
                throw;
            if (existing.BindingId != attempt.BindingId || existing.EvidenceBundleId != attempt.EvidenceBundleId)
                throw new HealingIdempotencyConflictException("Repair attempt identity was reused with different authority or evidence.");
            return new HealingStoreWriteResult<RepairAttempt>(existing, true);
        }
    }

    public async ValueTask<RepairAttemptStoreCreateResult> TryCreateAttemptAsync(
        RepairAttempt attempt,
        int maximumAttempts,
        int maximumConcurrentAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (maximumAttempts is < 1 or > HealingBudgetOptions.MaximumRepairAttempts)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (maximumConcurrentAttempts is < 1 or > HealingBudgetOptions.MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentAttempts));

        async Task<RepairAttemptStoreCreateResult> CreateAsync()
        {
            if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                    dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken))
                return new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.Conflict, null);

            var activeApplicationAttempts = await dbContext.RepairAttempts.CountAsync(x =>
                x.WorkspaceId == attempt.WorkspaceId &&
                x.ApplicationId == attempt.ApplicationId &&
                (x.Status == RepairAttemptStatus.Queued || x.Status == RepairAttemptStatus.Dispatched ||
                 x.Status == RepairAttemptStatus.Running || x.Status == RepairAttemptStatus.ProposalReady ||
                 x.Status == RepairAttemptStatus.ResultReceived || x.Status == RepairAttemptStatus.Publishing),
                cancellationToken);
            if (activeApplicationAttempts >= maximumConcurrentAttempts)
                return new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.ConcurrencyLimitReached, null);

            var attemptCount = await dbContext.RepairAttempts.CountAsync(
                x => x.WorkspaceId == attempt.WorkspaceId &&
                     x.ApplicationId == attempt.ApplicationId &&
                     x.EpisodeId == attempt.EpisodeId &&
                     x.TargetRevision == attempt.TargetRevision,
                cancellationToken);
            var activeAttemptExists = await dbContext.RepairAttempts.AnyAsync(
                x => x.WorkspaceId == attempt.WorkspaceId &&
                     x.ApplicationId == attempt.ApplicationId &&
                     x.EpisodeId == attempt.EpisodeId &&
                     x.TargetRevision == attempt.TargetRevision &&
                     (x.Status == RepairAttemptStatus.Queued || x.Status == RepairAttemptStatus.Dispatched ||
                      x.Status == RepairAttemptStatus.Running || x.Status == RepairAttemptStatus.ProposalReady ||
                      x.Status == RepairAttemptStatus.ResultReceived ||
                      x.Status == RepairAttemptStatus.Publishing || x.Status == RepairAttemptStatus.PullRequestOpen),
                cancellationToken);
            if (activeAttemptExists)
                return new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.Conflict, null);
            if (attemptCount >= maximumAttempts)
                return new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.AttemptLimitReached, null);

            attempt.AttemptNumber = attemptCount + 1;
            attempt.Version = NewVersion();
            dbContext.RepairAttempts.Add(attempt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.Created, attempt);
        }

        try
        {
            if (dbContext.Database.CurrentTransaction is not null)
                return await CreateAsync();

            var executionStrategy = dbContext.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var result = await CreateAsync();
                if (result.Outcome != RepairAttemptStoreOutcome.Created)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return result;
                }
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var count = await dbContext.RepairAttempts.AsNoTracking().CountAsync(
                x => x.WorkspaceId == attempt.WorkspaceId &&
                     x.ApplicationId == attempt.ApplicationId &&
                     x.EpisodeId == attempt.EpisodeId &&
                     x.TargetRevision == attempt.TargetRevision,
                cancellationToken);
            return count >= maximumAttempts
                ? new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.AttemptLimitReached, null)
                : new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.Conflict, null);
        }
    }

    public ValueTask<RepairAttempt?> FindAttemptAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.RepairAttempts.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == attemptId,
            cancellationToken));

    public async ValueTask<bool> TryAcquireLeaseAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseOwner,
        string leaseTokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.RepairAttempts
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.Id == attemptId &&
                        x.Status != RepairAttemptStatus.Succeeded &&
                        x.Status != RepairAttemptStatus.Failed &&
                        x.Status != RepairAttemptStatus.Stopped &&
                        x.Status != RepairAttemptStatus.Expired &&
                        (x.LeaseToken == null || x.LeaseExpiresAt < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RepairAttemptStatus.Running)
                .SetProperty(x => x.LeaseOwner, leaseOwner)
                .SetProperty(x => x.LeaseToken, leaseTokenHash)
                .SetProperty(x => x.LeaseExpiresAt, expiresAt)
                .SetProperty(x => x.StartedAt, x => x.StartedAt ?? now)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<bool> TryHeartbeatLeaseAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseTokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.RepairAttempts
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.Id == attemptId &&
                        x.Status == RepairAttemptStatus.Running &&
                        x.LeaseToken == leaseTokenHash &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseExpiresAt, expiresAt)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<bool> TryRecordReproductionAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseTokenHash,
        RepairClassification classification,
        string reproductionJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.RepairAttempts
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.Id == attemptId &&
                        x.Status == RepairAttemptStatus.Running &&
                        x.LeaseToken == leaseTokenHash &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RepairClassification, classification)
                .SetProperty(x => x.OutcomeCode, "reproduction-recorded")
                .SetProperty(x => x.SafeOutcomeDetail, reproductionJson)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<bool> TryAppendBundleAsync(
        EvidenceBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Id == Guid.Empty || bundle.WorkspaceId == Guid.Empty || bundle.ApplicationId == Guid.Empty || bundle.IncidentId == Guid.Empty)
            throw new ArgumentException("Evidence bundle scope is required.", nameof(bundle));
        if (await dbContext.EvidenceBundles.AsNoTracking().AnyAsync(x => x.Id == bundle.Id, cancellationToken))
            return false;

        dbContext.EvidenceBundles.Add(bundle);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(bundle).State = EntityState.Detached;
            if (await dbContext.EvidenceBundles.AsNoTracking().AnyAsync(
                    x => x.Id == bundle.Id,
                    cancellationToken))
                return false;
            throw;
        }
    }

    public ValueTask<EvidenceBundle?> FindBundleAsync(
        Guid workspaceId,
        Guid bundleId,
        CancellationToken cancellationToken = default) =>
        new(dbContext.EvidenceBundles.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == bundleId,
            cancellationToken));

    public async ValueTask<bool> TryAppendElevatedBundleAsync(
        EvidenceBundle bundle,
        EvidenceAccessDecision decision,
        Guid targetAttemptId,
        Guid expectedBaseBundleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(decision);
        if (bundle.Tier != EvidenceTier.Elevated ||
            !decision.Authorized ||
            decision.ReleasedBundleId != bundle.Id ||
            decision.WorkspaceId != bundle.WorkspaceId ||
            decision.ApplicationId != bundle.ApplicationId ||
            decision.IncidentId != bundle.IncidentId)
        {
            throw new ArgumentException("An elevated bundle requires its matching authorized access decision.");
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var conflict = await dbContext.EvidenceBundles.AsNoTracking().AnyAsync(
                    x => x.Id == bundle.Id,
                    cancellationToken);
                if (conflict)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
                    x => x.WorkspaceId == bundle.WorkspaceId &&
                         x.ApplicationId == bundle.ApplicationId &&
                         x.IncidentId == bundle.IncidentId &&
                         x.Id == targetAttemptId &&
                         x.EvidenceBundleId == expectedBaseBundleId,
                    cancellationToken);
                if (attempt is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                dbContext.EvidenceBundles.Add(bundle);
                dbContext.EvidenceAccessDecisions.Add(decision);
                attempt.EvidenceBundleId = bundle.Id;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            });
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var conflict = await dbContext.EvidenceBundles.AsNoTracking().AnyAsync(
                    x => x.Id == bundle.Id,
                    cancellationToken);
            var targetStillEligible = await dbContext.RepairAttempts.AsNoTracking().AnyAsync(
                x => x.WorkspaceId == bundle.WorkspaceId &&
                     x.ApplicationId == bundle.ApplicationId &&
                     x.IncidentId == bundle.IncidentId &&
                     x.Id == targetAttemptId &&
                     x.EvidenceBundleId == expectedBaseBundleId,
                cancellationToken);
            if (conflict || !targetStillEligible)
                return false;
            throw;
        }
    }

    public async ValueTask AppendAccessDecisionAsync(
        EvidenceAccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Id == Guid.Empty || decision.WorkspaceId == Guid.Empty || decision.ApplicationId == Guid.Empty || decision.IncidentId == Guid.Empty)
            throw new ArgumentException("Evidence access decision scope is required.", nameof(decision));
        dbContext.EvidenceAccessDecisions.Add(decision);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<VerificationResult> UpsertVerificationAsync(
        VerificationResult verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var existing = await dbContext.VerificationResults.SingleOrDefaultAsync(
            x => x.WorkspaceId == verification.WorkspaceId &&
                 x.ApplicationId == verification.ApplicationId &&
                 x.EpisodeId == verification.EpisodeId &&
                 x.EnvironmentId == verification.EnvironmentId &&
                 x.RepairedRevision == verification.RepairedRevision, cancellationToken);
        if (existing is null)
        {
            verification.Version = NewVersion();
            dbContext.VerificationResults.Add(verification);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return verification;
            }
            catch (DbUpdateException exception)
            {
                dbContext.Entry(verification).State = EntityState.Detached;
                existing = await dbContext.VerificationResults.AsNoTracking().SingleOrDefaultAsync(
                    x => x.WorkspaceId == verification.WorkspaceId &&
                         x.ApplicationId == verification.ApplicationId &&
                         x.EpisodeId == verification.EpisodeId &&
                         x.EnvironmentId == verification.EnvironmentId &&
                         x.RepairedRevision == verification.RepairedRevision, cancellationToken);
                if (existing is null)
                    throw;
                if (VerificationsEquivalent(existing, verification))
                    return existing;
                throw ConcurrentCreate("Verification result", exception);
            }
        }

        if (verification.Version.Length == 0)
        {
            if (VerificationsEquivalent(existing, verification))
                return existing;
            throw ConcurrentCreate("Verification result");
        }
        EnsureExpectedVersion(verification.Version, existing.Version, "Verification result");

        existing.WindowStartedAt = verification.WindowStartedAt;
        existing.WindowEndsAt = verification.WindowEndsAt;
        existing.RelevantOperationSuccessCount = verification.RelevantOperationSuccessCount;
        existing.LastRelevantOperationSuccessAt = verification.LastRelevantOperationSuccessAt;
        existing.RecurrenceCount = verification.RecurrenceCount;
        existing.LastRecurrenceAt = verification.LastRecurrenceAt;
        existing.Outcome = verification.Outcome;
        existing.DeploymentObservationId = verification.DeploymentObservationId;
        existing.SupportingOccurrenceId = verification.SupportingOccurrenceId;
        existing.DecidedAt = verification.DecidedAt;
        existing.SafeDecisionReason = verification.SafeDecisionReason;
        existing.WaiverExpiresAt = verification.WaiverExpiresAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async ValueTask<HealingStoreWriteResult<DeploymentObservation>> AppendDeploymentObservationAsync(
        DeploymentObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var existing = await dbContext.DeploymentObservations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == observation.WorkspaceId &&
                 x.ApplicationId == observation.ApplicationId &&
                 x.Source == observation.Source &&
                 (x.SourceIdempotencyKey == observation.SourceIdempotencyKey ||
                  x.SourceObservationId == observation.SourceObservationId), cancellationToken);
        if (existing is not null)
        {
            var candidateHash = $"{observation.EnvironmentId:N}:{observation.Revision}:{observation.DeployedAt:O}:{observation.SourceObservationId}:{observation.SourceIdempotencyKey}:{observation.TrustIdentity}:{observation.EvidenceDigest}";
            var existingHash = $"{existing.EnvironmentId:N}:{existing.Revision}:{existing.DeployedAt:O}:{existing.SourceObservationId}:{existing.SourceIdempotencyKey}:{existing.TrustIdentity}:{existing.EvidenceDigest}";
            return MatchReplay(existing, candidateHash, existingHash, "Deployment observation");
        }

        dbContext.DeploymentObservations.Add(observation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<DeploymentObservation>(observation, false);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(observation).State = EntityState.Detached;
            existing = await dbContext.DeploymentObservations.AsNoTracking().SingleOrDefaultAsync(
                x => x.WorkspaceId == observation.WorkspaceId &&
                     x.ApplicationId == observation.ApplicationId &&
                     x.Source == observation.Source &&
                     (x.SourceIdempotencyKey == observation.SourceIdempotencyKey ||
                      x.SourceObservationId == observation.SourceObservationId), cancellationToken);
            if (existing is null)
                throw;
            var candidateHash = $"{observation.EnvironmentId:N}:{observation.Revision}:{observation.DeployedAt:O}:{observation.SourceObservationId}:{observation.SourceIdempotencyKey}:{observation.TrustIdentity}:{observation.EvidenceDigest}";
            var existingHash = $"{existing.EnvironmentId:N}:{existing.Revision}:{existing.DeployedAt:O}:{existing.SourceObservationId}:{existing.SourceIdempotencyKey}:{existing.TrustIdentity}:{existing.EvidenceDigest}";
            return MatchReplay(existing, candidateHash, existingHash, "Deployment observation");
        }
    }

    public async ValueTask<HealingStoreWriteResult<HealingSignalInboxItem>> AppendInboxAsync(
        HealingSignalInboxItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var existing = await dbContext.HealingSignalInboxItems.SingleOrDefaultAsync(
            x => x.WorkspaceId == item.WorkspaceId &&
                 x.ApplicationId == item.ApplicationId &&
                 x.IdempotencyKey == item.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
            return MatchReplay(existing, item.EnvelopeHash, existing.EnvelopeHash, "Healing signal inbox");

        item.Version = NewVersion();
        dbContext.HealingSignalInboxItems.Add(item);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<HealingSignalInboxItem>(item, false);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(item).State = EntityState.Detached;
            existing = await dbContext.HealingSignalInboxItems.SingleOrDefaultAsync(
                x => x.WorkspaceId == item.WorkspaceId &&
                     x.ApplicationId == item.ApplicationId &&
                     x.IdempotencyKey == item.IdempotencyKey,
                cancellationToken);
            if (existing is null)
                throw;
            return MatchReplay(existing, item.EnvelopeHash, existing.EnvelopeHash, "Healing signal inbox");
        }
    }

    public async ValueTask<HealingStoreLease<HealingSignalInboxItem>?> TryLeaseNextInboxAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
            throw new ArgumentException("Lease owner is required.", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var candidateIds = await dbContext.HealingSignalInboxItems.AsNoTracking()
            .Where(x =>
                (x.Status == HealingInboxStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                (x.Status == HealingInboxStatus.Leased && x.LeaseExpiresAt < now))
            .OrderBy(x => x.AcceptedAt)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(8)
            .ToListAsync(cancellationToken);

        foreach (var candidateId in candidateIds)
        {
            var leaseToken = Guid.NewGuid().ToString("N");
            var leaseExpiresAt = now.Add(leaseDuration);
            var updated = await dbContext.HealingSignalInboxItems
                .Where(x => x.Id == candidateId &&
                    ((x.Status == HealingInboxStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                     (x.Status == HealingInboxStatus.Leased && x.LeaseExpiresAt < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, HealingInboxStatus.Leased)
                    .SetProperty(x => x.LeaseOwner, leaseOwner)
                    .SetProperty(x => x.LeaseToken, leaseToken)
                    .SetProperty(x => x.LeaseExpiresAt, leaseExpiresAt)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.Version, NewVersion()), cancellationToken);
            if (updated == 0)
                continue;

            var leased = await dbContext.HealingSignalInboxItems.AsNoTracking()
                .SingleAsync(x => x.Id == candidateId, cancellationToken);
            return new HealingStoreLease<HealingSignalInboxItem>(leased, leaseToken);
        }

        return null;
    }

    public async ValueTask<bool> CompleteInboxAsync(
        Guid itemId,
        string leaseToken,
        DateTimeOffset now,
        HealingInboxStatus terminalStatus,
        string? outcomeCode,
        string? safeOutcomeDetail,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus is not (HealingInboxStatus.Completed or HealingInboxStatus.Rejected or HealingInboxStatus.DeadLettered))
            throw new ArgumentOutOfRangeException(nameof(terminalStatus), "Inbox completion requires a terminal status.");

        var updated = await dbContext.HealingSignalInboxItems
            .Where(x => x.Id == itemId &&
                        x.Status == HealingInboxStatus.Leased &&
                        x.LeaseToken == leaseToken &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, terminalStatus)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.SafeOutcomeDetail, safeOutcomeDetail)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<HealingInboxLease?> TryLeaseNextAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var lease = await TryLeaseNextInboxAsync(leaseOwner, now, leaseDuration, cancellationToken);
        return lease is null ? null : new HealingInboxLease(lease.Value, lease.LeaseToken);
    }

    public ValueTask<bool> CompleteAsync(
        Guid itemId,
        string leaseToken,
        DateTimeOffset now,
        HealingInboxStatus terminalStatus,
        string outcomeCode,
        string? safeOutcomeDetail,
        CancellationToken cancellationToken = default) =>
        CompleteInboxAsync(itemId, leaseToken, now, terminalStatus, outcomeCode, safeOutcomeDetail, cancellationToken);

    public async ValueTask<bool> RetryAsync(
        Guid itemId,
        string leaseToken,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt,
        string outcomeCode,
        string? safeOutcomeDetail,
        CancellationToken cancellationToken = default)
    {
        if (nextAttemptAt < now)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        var updated = await dbContext.HealingSignalInboxItems
            .Where(x => x.Id == itemId &&
                        x.Status == HealingInboxStatus.Leased &&
                        x.LeaseToken == leaseToken &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HealingInboxStatus.Pending)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, nextAttemptAt)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.SafeOutcomeDetail, safeOutcomeDetail)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<HealingStoreWriteResult<ProviderOperation>> AppendProviderOperationAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ProviderConnectionId == operation.ProviderConnectionId &&
                 x.Kind == operation.Kind &&
                 x.IdempotencyKey == operation.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
            return MatchReplay(existing, operation.PayloadHash, existing.PayloadHash, "Provider operation");

        operation.Version = NewVersion();
        dbContext.ProviderOperations.Add(operation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new HealingStoreWriteResult<ProviderOperation>(operation, false);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(operation).State = EntityState.Detached;
            existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(
                x => x.WorkspaceId == operation.WorkspaceId &&
                     x.ProviderConnectionId == operation.ProviderConnectionId &&
                     x.Kind == operation.Kind &&
                     x.IdempotencyKey == operation.IdempotencyKey,
                cancellationToken);
            if (existing is null)
                throw;
            return MatchReplay(existing, operation.PayloadHash, existing.PayloadHash, "Provider operation");
        }
    }

    public async ValueTask<HealingStoreLease<ProviderOperation>?> TryLeaseNextProviderOperationAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
            throw new ArgumentException("Lease owner is required.", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var candidateIds = await dbContext.ProviderOperations.AsNoTracking()
            .Where(x =>
                (x.Status == ProviderOperationStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                (x.Status == ProviderOperationStatus.Leased && x.LeaseExpiresAt < now))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(8)
            .ToListAsync(cancellationToken);
        foreach (var candidateId in candidateIds)
        {
            var leaseToken = Guid.NewGuid().ToString("N");
            var updated = await dbContext.ProviderOperations
                .Where(x => x.Id == candidateId &&
                    ((x.Status == ProviderOperationStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                     (x.Status == ProviderOperationStatus.Leased && x.LeaseExpiresAt < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ProviderOperationStatus.Leased)
                    .SetProperty(x => x.LeaseOwner, leaseOwner)
                    .SetProperty(x => x.LeaseToken, leaseToken)
                    .SetProperty(x => x.LeaseExpiresAt, now.Add(leaseDuration))
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Version, NewVersion()), cancellationToken);
            if (updated == 0)
                continue;

            var leased = await dbContext.ProviderOperations.AsNoTracking()
                .SingleAsync(x => x.Id == candidateId, cancellationToken);
            return new HealingStoreLease<ProviderOperation>(leased, leaseToken);
        }
        return null;
    }

    public async ValueTask<bool> CompleteProviderOperationAsync(
        Guid operationId,
        string leaseToken,
        DateTimeOffset now,
        ProviderOperationStatus terminalStatus,
        string? providerCorrelationId,
        string? outcomeCode,
        string? safeError,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus is not (ProviderOperationStatus.Completed or ProviderOperationStatus.Failed or ProviderOperationStatus.DeadLettered))
            throw new ArgumentOutOfRangeException(nameof(terminalStatus), "Provider operation completion requires a terminal status.");

        var updated = await dbContext.ProviderOperations
            .Where(x => x.Id == operationId &&
                        x.Status == ProviderOperationStatus.Leased &&
                        x.LeaseToken == leaseToken &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, terminalStatus)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.ProviderCorrelationId, providerCorrelationId)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.SafeError, safeError)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    public async ValueTask<bool> RetryProviderOperationAsync(
        Guid operationId,
        string leaseToken,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt,
        string outcomeCode,
        string? safeError,
        CancellationToken cancellationToken = default)
    {
        if (nextAttemptAt < now)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));

        var updated = await dbContext.ProviderOperations
            .Where(x => x.Id == operationId &&
                        x.Status == ProviderOperationStatus.Leased &&
                        x.LeaseToken == leaseToken &&
                        x.LeaseExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ProviderOperationStatus.Pending)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, nextAttemptAt)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.SafeError, safeError)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
        return updated == 1;
    }

    async ValueTask<ProviderOperationAppendResult> IProviderOperationStore.AppendAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken)
    {
        var result = await AppendProviderOperationAsync(operation, cancellationToken);
        return new ProviderOperationAppendResult(result.Value, result.IsReplay);
    }

    async ValueTask<int> IHealingLeasedOperationStore<ProviderOperation>.RecoverStaleLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProviderOperations
            .Where(x => x.Status == ProviderOperationStatus.Leased && x.LeaseExpiresAt < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ProviderOperationStatus.Pending)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, NewVersion()), cancellationToken);
    }

    async ValueTask<HealingOperationLease<ProviderOperation>?> IHealingLeasedOperationStore<ProviderOperation>.TryLeaseNextAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var lease = await TryLeaseNextProviderOperationAsync(workerId, now, leaseDuration, cancellationToken);
        return lease is null
            ? null
            : new HealingOperationLease<ProviderOperation>(
                lease.Value.Id,
                lease.LeaseToken,
                lease.Value,
                lease.Value.AttemptCount,
                1);
    }

    async ValueTask IHealingLeasedOperationStore<ProviderOperation>.FinishAsync(
        HealingOperationLease<ProviderOperation> lease,
        HealingOperationOutcome outcome,
        DateTimeOffset finishedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var completed = outcome.Disposition switch
        {
            HealingOperationDisposition.Completed => await CompleteProviderOperationAsync(
                lease.OperationId, lease.LeaseToken, finishedAt, ProviderOperationStatus.Completed,
                lease.Operation.ProviderCorrelationId, outcome.OutcomeCode, outcome.SafeDetail, cancellationToken),
            HealingOperationDisposition.DeadLettered => await CompleteProviderOperationAsync(
                lease.OperationId, lease.LeaseToken, finishedAt, ProviderOperationStatus.DeadLettered,
                lease.Operation.ProviderCorrelationId, outcome.OutcomeCode, outcome.SafeDetail, cancellationToken),
            HealingOperationDisposition.Retry when nextAttemptAt is not null => await RetryProviderOperationAsync(
                lease.OperationId, lease.LeaseToken, finishedAt, nextAttemptAt.Value,
                outcome.OutcomeCode, outcome.SafeDetail, cancellationToken),
            _ => throw new InvalidOperationException("A retry outcome requires a next-attempt timestamp.")
        };

        if (!completed)
            throw new DbUpdateConcurrencyException("The provider operation lease was lost before completion.");
    }

    public async ValueTask<HealingAuditEvent> AppendAsync(
        HealingAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        const int maxCollisions = 8;
        for (var attempt = 0; attempt < maxCollisions; attempt++)
        {
            var replay = await FindAuditReplayAsync(auditEvent, cancellationToken);
            if (replay is not null)
                return MatchAuditReplay(replay, auditEvent);

            auditEvent.Sequence = await dbContext.HealingAuditEvents.AsNoTracking()
                .Where(x => x.WorkspaceId == auditEvent.WorkspaceId &&
                            x.AggregateType == auditEvent.AggregateType &&
                            x.AggregateId == auditEvent.AggregateId)
                .Select(x => (long?)x.Sequence)
                .MaxAsync(cancellationToken) is { } current ? current + 1 : 1;
            dbContext.HealingAuditEvents.Add(auditEvent);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return auditEvent;
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(auditEvent).State = EntityState.Detached;
                replay = await FindAuditReplayAsync(auditEvent, cancellationToken);
                if (replay is not null)
                    return MatchAuditReplay(replay, auditEvent);
                var sequenceCollision = await dbContext.HealingAuditEvents.AsNoTracking().AnyAsync(
                    x => x.WorkspaceId == auditEvent.WorkspaceId &&
                         x.AggregateType == auditEvent.AggregateType &&
                         x.AggregateId == auditEvent.AggregateId &&
                         x.Sequence == auditEvent.Sequence,
                    cancellationToken);
                if (!sequenceCollision)
                    throw;
                await Task.Yield();
            }
        }

        throw new DbUpdateConcurrencyException("Could not allocate a Healing audit sequence after bounded collision retries.");
    }

    private ValueTask<HealingAuditEvent?> FindAuditReplayAsync(
        HealingAuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        new(dbContext.HealingAuditEvents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == auditEvent.WorkspaceId &&
                 x.AggregateType == auditEvent.AggregateType &&
                 x.AggregateId == auditEvent.AggregateId &&
                 x.EventType == auditEvent.EventType &&
                 x.CorrelationId == auditEvent.CorrelationId,
            cancellationToken));

    private static HealingAuditEvent MatchAuditReplay(
        HealingAuditEvent existing,
        HealingAuditEvent candidate)
    {
        if (existing.ReasonCode != candidate.ReasonCode ||
            existing.ActorType != candidate.ActorType ||
            existing.ActorId != candidate.ActorId ||
            existing.CausationId != candidate.CausationId ||
            existing.PolicyVersion != candidate.PolicyVersion ||
            existing.InputHash != candidate.InputHash ||
            existing.OutputHash != candidate.OutputHash ||
            existing.SafeDetailJson != candidate.SafeDetailJson)
            throw new HealingIdempotencyConflictException(
                "Healing audit idempotency identity was reused with different decision details.");
        return existing;
    }

    public async ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(
        HealingAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var events = dbContext.HealingAuditEvents.AsNoTracking().Where(x => x.WorkspaceId == query.WorkspaceId);
        if (query.AggregateId is { } aggregateId)
            events = events.Where(x => x.AggregateId == aggregateId);
        if (query.CorrelationId is { } correlationId)
            events = events.Where(x => x.CorrelationId == correlationId);
        return await events.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Sequence)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    private static HealingStoreWriteResult<T> MatchReplay<T>(
        T existing,
        string candidateHash,
        string existingHash,
        string operation)
    {
        if (!string.Equals(candidateHash, existingHash, StringComparison.Ordinal))
            throw new HealingIdempotencyConflictException($"{operation} idempotency key was reused with a different payload hash.");
        return new HealingStoreWriteResult<T>(existing, true);
    }

    private static byte[] NewVersion() => Guid.NewGuid().ToByteArray();

    private static void ValidateTelemetrySource(HealingTelemetrySource source)
    {
        if (source.WorkspaceId == Guid.Empty || source.ApplicationId == Guid.Empty || source.EnvironmentId == Guid.Empty)
            throw new ArgumentException("Telemetry source scope is required.", nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Name);
        ValidateCredential(source.CredentialSalt, source.CredentialHash);
        if (source.CredentialVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(source), "Telemetry source credential version must be positive.");
    }

    private static void ValidateCredential(byte[] salt, byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(hash);
        if (salt.Length != 32 || hash.Length != 32)
            throw new ArgumentException("Telemetry source credential salts and hashes must contain exactly 32 bytes.");
    }

    private static void EnsureExpectedVersion(byte[] expected, byte[] current, string aggregate)
    {
        if (expected.Length == 0 || !expected.AsSpan().SequenceEqual(current))
            throw new DbUpdateConcurrencyException($"{aggregate} was modified by another operation.");
    }

    private static bool ConfigurationsEquivalent(HealingConfiguration left, HealingConfiguration right)
    {
        if (left.WorkspaceId != right.WorkspaceId ||
            left.ApplicationId != right.ApplicationId ||
            left.DiscoveryEnabled != right.DiscoveryEnabled ||
            left.RepairEnabled != right.RepairEnabled ||
            left.AutomaticMergeEnabled != right.AutomaticMergeEnabled ||
            left.SignalProfileVersion != right.SignalProfileVersion ||
            left.DefaultAttemptLimit != right.DefaultAttemptLimit ||
            left.VerificationWindow != right.VerificationWindow ||
            left.TimeBudget != right.TimeBudget ||
            left.ConcurrencyBudget != right.ConcurrencyBudget ||
            left.InferenceBudget != right.InferenceBudget ||
            left.RepositoryRunBudget != right.RepositoryRunBudget ||
            left.ApplicationKillSwitch != right.ApplicationKillSwitch ||
            left.ClassificationPolicyJson != right.ClassificationPolicyJson ||
            left.Environments.Count != right.Environments.Count)
            return false;

        var rightEnvironments = right.Environments.ToDictionary(x => x.EnvironmentId);
        return left.Environments.All(environment =>
            rightEnvironments.TryGetValue(environment.EnvironmentId, out var candidate) &&
            environment.WorkspaceId == candidate.WorkspaceId &&
            environment.ApplicationId == candidate.ApplicationId &&
            environment.DiscoveryEnabled == candidate.DiscoveryEnabled &&
            environment.RepairEnabled == candidate.RepairEnabled &&
            environment.OccurrenceThreshold == candidate.OccurrenceThreshold &&
            environment.DebounceWindow == candidate.DebounceWindow &&
            environment.EnvironmentKillSwitch == candidate.EnvironmentKillSwitch &&
            environment.ClassificationPolicyJson == candidate.ClassificationPolicyJson);
    }

    private static bool VerificationsEquivalent(VerificationResult left, VerificationResult right) =>
        left.WorkspaceId == right.WorkspaceId &&
        left.ApplicationId == right.ApplicationId &&
        left.EpisodeId == right.EpisodeId &&
        left.EnvironmentId == right.EnvironmentId &&
        left.RepairedRevision == right.RepairedRevision &&
        left.WindowStartedAt == right.WindowStartedAt &&
        left.WindowEndsAt == right.WindowEndsAt &&
        left.RelevantOperationSuccessCount == right.RelevantOperationSuccessCount &&
        left.LastRelevantOperationSuccessAt == right.LastRelevantOperationSuccessAt &&
        left.RecurrenceCount == right.RecurrenceCount &&
        left.LastRecurrenceAt == right.LastRecurrenceAt &&
        left.Outcome == right.Outcome &&
        left.DeploymentObservationId == right.DeploymentObservationId &&
        left.SupportingOccurrenceId == right.SupportingOccurrenceId &&
        left.DecidedAt == right.DecidedAt &&
        left.SafeDecisionReason == right.SafeDecisionReason &&
        left.WaiverExpiresAt == right.WaiverExpiresAt;

    private static void ValidateEnvironmentOverrides(HealingConfiguration configuration)
    {
        if (configuration.Environments.Select(x => x.EnvironmentId).Distinct().Count() != configuration.Environments.Count)
            throw new ArgumentException("Healing environment overrides must be unique by environment.", nameof(configuration));
        if (configuration.Environments.Any(x => x.WorkspaceId != configuration.WorkspaceId || x.ApplicationId != configuration.ApplicationId))
            throw new ArgumentException("Healing environment overrides must use the configuration workspace and application.", nameof(configuration));
    }

    private void ReconcileEnvironmentOverrides(
        HealingConfiguration existing,
        IReadOnlyCollection<HealingEnvironmentConfiguration> desired)
    {
        var desiredEnvironmentIds = desired.Select(x => x.EnvironmentId).ToHashSet();
        foreach (var removed in existing.Environments.Where(x => !desiredEnvironmentIds.Contains(x.EnvironmentId)).ToList())
            dbContext.HealingEnvironmentConfigurations.Remove(removed);

        foreach (var requested in desired)
        {
            var current = existing.Environments.SingleOrDefault(x => x.EnvironmentId == requested.EnvironmentId);
            if (current is null)
            {
                requested.Id = requested.Id == Guid.Empty ? Guid.NewGuid() : requested.Id;
                requested.HealingConfigurationId = existing.Id;
                existing.Environments.Add(requested);
                dbContext.HealingEnvironmentConfigurations.Add(requested);
                continue;
            }

            current.DiscoveryEnabled = requested.DiscoveryEnabled;
            current.RepairEnabled = requested.RepairEnabled;
            current.OccurrenceThreshold = requested.OccurrenceThreshold;
            current.DebounceWindow = requested.DebounceWindow;
            current.EnvironmentKillSwitch = requested.EnvironmentKillSwitch;
            current.ClassificationPolicyJson = requested.ClassificationPolicyJson;
            current.UpdatedAt = requested.UpdatedAt;
        }
    }

    private void DetachManifestGraph(ComponentManifestModel manifest)
    {
        var entryIds = manifest.Entries.Select(x => x.Id).ToHashSet();
        var assemblyIds = manifest.Entries.SelectMany(x => x.Assemblies).Select(x => x.Id).ToHashSet();
        var dependencyIds = manifest.Dependencies.Select(x => x.Id).ToHashSet();
        foreach (var entry in dbContext.ChangeTracker.Entries<ComponentManifestAssemblyArtifact>()
                     .Where(x => x.Entity.ManifestId == manifest.Id || assemblyIds.Contains(x.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in dbContext.ChangeTracker.Entries<ComponentDependency>()
                     .Where(x => x.Entity.ManifestId == manifest.Id || dependencyIds.Contains(x.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in dbContext.ChangeTracker.Entries<ComponentManifestEntry>()
                     .Where(x => x.Entity.ManifestId == manifest.Id || entryIds.Contains(x.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
        dbContext.Entry(manifest).State = EntityState.Detached;
    }

    private void DetachConfigurationGraph(HealingConfiguration configuration)
    {
        var environmentIds = configuration.Environments.Select(x => x.Id).ToHashSet();
        foreach (var entry in dbContext.ChangeTracker.Entries<HealingEnvironmentConfiguration>()
                     .Where(x => x.Entity.HealingConfigurationId == configuration.Id || environmentIds.Contains(x.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
        dbContext.Entry(configuration).State = EntityState.Detached;
    }

    private static DbUpdateConcurrencyException ConcurrentCreate(string aggregate, Exception? innerException = null) =>
        new($"{aggregate} was created concurrently with a different request.", innerException);

    private IQueryable<HealingIncident> ActiveIncidents() => dbContext.HealingIncidents.AsNoTracking()
        .Where(x => x.Status != HealingIncidentStatus.Failed &&
                    x.Status != HealingIncidentStatus.Healed &&
                    x.Status != HealingIncidentStatus.Superseded &&
                    x.Status != HealingIncidentStatus.Waived);

    private static HealingIncidentStatus InitialIncidentStatus(HealingIncidentProjectionRequest request)
    {
        if (request.SelectedBindingId is null || request.SelectedComponentEntryId is null || request.ProviderConnectionId is null)
            return HealingIncidentStatus.ObservationOnly;
        if (request.OccurrenceThreshold > 1 || request.DebounceWindow > TimeSpan.Zero)
            return HealingIncidentStatus.ThresholdPending;
        return HealingIncidentStatus.ReadyForRepair;
    }

    private static void ValidateProjectionRequest(HealingIncidentProjectionRequest request)
    {
        if (request.InboxItemId == Guid.Empty || request.WorkspaceId == Guid.Empty ||
            request.ApplicationId == Guid.Empty || request.EnvironmentId == Guid.Empty)
            throw new ArgumentException("Projection scope and inbox identity are required.", nameof(request));
        if (request.OccurrenceThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Occurrence threshold must be positive.");
        if (request.DebounceWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Debounce window cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OccurrenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FingerprintVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepairRepositoryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClassificationPolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClassificationPolicyHash);
    }

    private static bool IsRetryableProjectionFailure(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return true;
        var message = exception.ToString();
        return message.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("SQLITE_BUSY", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("SQLITE_LOCKED", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureSqliteWriterTimeout()
    {
        const int minimumWriterTimeoutSeconds = 30;
        if (dbContext.Database.GetDbConnection() is Microsoft.Data.Sqlite.SqliteConnection connection &&
            connection.DefaultTimeout < minimumWriterTimeoutSeconds)
        {
            // SQLite permits only one writer. Give queued projection transactions enough time to acquire
            // that writer lock before the bounded retry loop recreates their EF state.
            connection.DefaultTimeout = minimumWriterTimeoutSeconds;
        }
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static string SerializeRevisions(Guid? revisionId) =>
        JsonSerializer.Serialize(revisionId is null ? Array.Empty<Guid>() : new[] { revisionId.Value });

    private static string AddRevision(string currentJson, Guid? revisionId)
    {
        if (revisionId is null)
            return currentJson;
        var revisions = JsonSerializer.Deserialize<Guid[]>(currentJson) ?? [];
        return revisions.Contains(revisionId.Value)
            ? currentJson
            : JsonSerializer.Serialize(revisions.Append(revisionId.Value).Order().ToArray());
    }
}
