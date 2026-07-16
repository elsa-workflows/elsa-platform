using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Security;
using Microsoft.EntityFrameworkCore;
using System.Data;
using ComponentManifestModel = Elsa.Platform.Healing.Core.ComponentManifest;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed record HealingStoreWriteResult<T>(T Value, bool IsReplay);

public sealed record HealingStoreLease<T>(T Value, string LeaseToken);

public sealed class HealingIdempotencyConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// Healing-owned durable store. Idempotency, atomic leases, and append-only audit behavior are kept behind
/// this interface so callers do not need provider-specific persistence knowledge.
/// </summary>
public sealed class HealingStore(HealingDbContext dbContext) : IHealingAuditStore, IHealingOwnershipStore, IHealingAdministrationStore
{
    public async ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

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
                                           x.FingerprintVersion == incident.FingerprintVersion &&
                                           x.Fingerprint == incident.Fingerprint &&
                                           x.RepairRepositoryKey == incident.RepairRepositoryKey, cancellationToken);
            if (existing is null)
                throw;
            return new HealingStoreWriteResult<HealingIncident>(existing, true);
        }
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

    public async ValueTask<VerificationResult> UpsertVerificationAsync(
        VerificationResult verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var existing = await dbContext.VerificationResults.SingleOrDefaultAsync(
            x => x.EpisodeId == verification.EpisodeId &&
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
                    x => x.EpisodeId == verification.EpisodeId &&
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
                 x.SourceIdempotencyKey == observation.SourceIdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var candidateHash = $"{observation.EnvironmentId:N}:{observation.Revision}:{observation.EvidenceDigest}";
            var existingHash = $"{existing.EnvironmentId:N}:{existing.Revision}:{existing.EvidenceDigest}";
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
                     x.SourceIdempotencyKey == observation.SourceIdempotencyKey, cancellationToken);
            if (existing is null)
                throw;
            var candidateHash = $"{observation.EnvironmentId:N}:{observation.Revision}:{observation.EvidenceDigest}";
            var existingHash = $"{existing.EnvironmentId:N}:{existing.Revision}:{existing.EvidenceDigest}";
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

    public async ValueTask<HealingStoreWriteResult<ProviderOperation>> AppendProviderOperationAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ProviderConnectionId == operation.ProviderConnectionId &&
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

    public async ValueTask<HealingAuditEvent> AppendAsync(
        HealingAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        const int maxCollisions = 8;
        for (var attempt = 0; attempt < maxCollisions; attempt++)
        {
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
            environment.EnvironmentKillSwitch == candidate.EnvironmentKillSwitch);
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
        left.DecidedAt == right.DecidedAt;

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
}
