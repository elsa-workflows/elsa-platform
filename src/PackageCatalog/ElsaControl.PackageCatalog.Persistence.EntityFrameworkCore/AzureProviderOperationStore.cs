using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Deployment.Azure;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class AzureProviderOperationStore(CatalogDbContext db) : IAzureProviderOperationStore
{
    private static readonly IReadOnlyDictionary<string, string> EmptySecretReferences =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public async Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var normalized = AzureProviderOperationValidation.Normalize(request);
        var hash = AzureProviderOperationValidation.ComputeRequestHash(normalized);
        var identity = AzureProviderOperationValidation.ComputeOperationIdentity(normalized);
        var existing = await FindByKeyAsync(normalized, cancellationToken);
        var identityEntity = await db.AzureProviderOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == normalized.WorkspaceId && x.TargetKey == normalized.TargetKey &&
                        x.OperationIdentity == identity &&
                        (x.Status == AzureProviderOperationStatus.Accepted || x.Status == AzureProviderOperationStatus.Queued ||
                         x.Status == AzureProviderOperationStatus.Running || x.Status == AzureProviderOperationStatus.RecoveryRequired))
            .SingleOrDefaultAsync(cancellationToken);
        existing ??= identityEntity is null ? null : ToModel(identityEntity);
        if (existing is not null) return EnsureSameRequest(existing, hash);

        var activeTargetEntity = await FindActiveTargetAsync(normalized, cancellationToken);
        if (activeTargetEntity is not null)
            throw new AzureProviderOperationConflictException(ToModel(activeTargetEntity));
        var previousResources = await GetLatestReconcileAsync(
            normalized.WorkspaceId,
            normalized.TargetKey,
            cancellationToken);

        var entity = new AzureProviderOperationEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = normalized.WorkspaceId,
            TargetKey = normalized.TargetKey,
            Action = normalized.Action,
            IdempotencyKey = normalized.IdempotencyKey,
            RequestHash = hash,
            OperationIdentity = identity,
            PlanFingerprint = normalized.PlanFingerprint,
            TemplateFingerprint = normalized.TemplateFingerprint,
            ElsaVersion = normalized.ElsaVersion,
            ReleaseLine = normalized.ReleaseLine,
            Topology = normalized.Topology,
            Isolation = normalized.Isolation,
            Location = normalized.Location,
            ImageRepository = normalized.ImageRepository,
            ImageDigest = normalized.ImageDigest,
            ReleaseManifestDigest = normalized.ReleaseManifestDigest,
            ReleaseManifestSignatureDigest = normalized.ReleaseManifestSignatureDigest,
            ReleaseManifestReference = normalized.ReleaseManifestReference,
            ReleaseManifestSignatureReference = normalized.ReleaseManifestSignatureReference,
            SecretReferencesJson = JsonSerializer.Serialize(normalized.SecretReferences),
            Status = AzureProviderOperationStatus.Accepted,
            Phase = AzureProviderOperationPhase.Planned,
            CheckpointSequence = 0,
            AttemptNumber = 0,
            Version = 1,
            Health = AzureProviderHealth.Unknown,
            CreatedAt = now,
            UpdatedAt = now,
            ResourceGroupName = previousResources?.Resources.ResourceGroupName,
            FoundationDeploymentId = previousResources?.Resources.FoundationDeploymentId,
            WorkloadDeploymentId = previousResources?.Resources.WorkloadDeploymentId,
            WorkloadResourceId = previousResources?.Resources.WorkloadResourceId,
            WorkloadRevisionName = previousResources?.Resources.WorkloadRevisionName,
            StableTrafficRevisionName = previousResources?.Resources.StableTrafficRevisionName
        };
        entity.Transitions.Add(new AzureProviderOperationTransitionEntity
        {
            Id = Guid.NewGuid(),
            OperationId = entity.Id,
            Sequence = 1,
            Status = entity.Status,
            Phase = entity.Phase,
            Code = "operation.accepted",
            Message = "Azure provider operation accepted.",
            OccurredAt = now
        });
        db.AzureProviderOperations.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ToModel(entity);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await FindByKeyAsync(normalized, cancellationToken);
            identityEntity = await db.AzureProviderOperations.AsNoTracking()
                .Where(x => x.WorkspaceId == normalized.WorkspaceId && x.TargetKey == normalized.TargetKey && x.OperationIdentity == identity &&
                            (x.Status == AzureProviderOperationStatus.Accepted || x.Status == AzureProviderOperationStatus.Queued ||
                             x.Status == AzureProviderOperationStatus.Running || x.Status == AzureProviderOperationStatus.RecoveryRequired))
                .SingleOrDefaultAsync(cancellationToken);
            existing ??= identityEntity is null ? null : ToModel(identityEntity);
            if (existing is not null) return EnsureSameRequest(existing, hash);
            activeTargetEntity = await FindActiveTargetAsync(normalized, cancellationToken);
            if (activeTargetEntity is not null)
                throw new AzureProviderOperationConflictException(ToModel(activeTargetEntity));
            throw;
        }
    }

    public async Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default) =>
        await db.AzureProviderOperations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken) is { } entity ? ToModel(entity) : null;

    public async Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var operations = await db.AzureProviderOperations.AsNoTracking()
            .Where(x => (x.Status == AzureProviderOperationStatus.Accepted ||
                         x.Status == AzureProviderOperationStatus.Queued) &&
                        x.CompletedAt == null &&
                        (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))
            .OrderBy(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return operations.Select(ToModel).ToList();
    }

    public async Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
            throw new ArgumentException("Target key is required.", nameof(targetKey));

        var normalizedTargetKey = targetKey.Trim().ToLowerInvariant();
        var entity = await db.AzureProviderOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.TargetKey == normalizedTargetKey &&
                        x.Action == AzureProviderOperationAction.Reconcile &&
                        (x.ResourceGroupName != null || x.FoundationDeploymentId != null ||
                         x.WorkloadDeploymentId != null || x.WorkloadResourceId != null ||
                         x.WorkloadRevisionName != null || x.StableTrafficRevisionName != null))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<AzureProviderOperation?> MarkUnrestorableAsync(
        Guid workspaceId,
        Guid operationId,
        DateTimeOffset now,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.AzureProviderOperations
            .Where(x => x.WorkspaceId == workspaceId && x.Id == operationId &&
                        (x.Status == AzureProviderOperationStatus.Accepted ||
                         x.Status == AzureProviderOperationStatus.Queued ||
                         x.Status == AzureProviderOperationStatus.RecoveryRequired) &&
                        (!expectedVersion.HasValue || x.Version == expectedVersion.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, x => x.Status == AzureProviderOperationStatus.RecoveryRequired
                    ? AzureProviderOperationStatus.RecoveryRequired
                    : AzureProviderOperationStatus.Failed)
                .SetProperty(x => x.CompletedAt, x => x.Status == AzureProviderOperationStatus.RecoveryRequired ? null : now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, x => x.Version + 1)
                .SetProperty(x => x.LeaseTokenHash, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.WorkerId, (string?)null)
                .SetProperty(x => x.CompletionLeaseTokenHash, (string?)null)
                .SetProperty(x => x.CompletionFingerprint, (string?)null), cancellationToken);
        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        db.ChangeTracker.Clear();
        var entity = await db.AzureProviderOperations
            .SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken);
        AddTransition(
            entity,
            "azure.plan.unrestorable",
            "The persisted provider plan cannot be restored.",
            now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToModel(entity);
    }

    public Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
        ClaimCoreAsync(workspaceId, operationId, workerId, leaseToken, leaseDuration, now, expectedVersion, allowRecovery: false, cancellationToken);

    public Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
        ClaimCoreAsync(workspaceId, operationId, workerId, leaseToken, leaseDuration, now, expectedVersion, allowRecovery: true, cancellationToken);

    private async Task<AzureProviderOperation?> ClaimCoreAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion, bool allowRecovery, CancellationToken cancellationToken)
    {
        AzureProviderOperationValidation.ValidateWorkerId(workerId);
        AzureProviderOperationValidation.ValidateLeaseToken(leaseToken);
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1)) throw new ArgumentException("Lease duration is required and bounded.");
        var hash = Hash(leaseToken);
        DateTimeOffset leaseExpires;
        try { leaseExpires = now.Add(leaseDuration); } catch (ArgumentOutOfRangeException) { throw new ArgumentException("Lease duration overflowed.", nameof(leaseDuration)); }
        AzureProviderOperation? result = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var changed = await db.AzureProviderOperations.Where(x => x.WorkspaceId == workspaceId && x.Id == operationId &&
                     (allowRecovery
                         ? x.Status == AzureProviderOperationStatus.RecoveryRequired
                         : x.Status == AzureProviderOperationStatus.Accepted || x.Status == AzureProviderOperationStatus.Queued) &&
                     (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now) && (!expectedVersion.HasValue || x.Version == expectedVersion.Value) &&
                     !db.AzureProviderOperations.Any(other => other.Id != operationId && other.WorkspaceId == workspaceId &&
                         other.TargetKey == x.TargetKey &&
                         (other.Status == AzureProviderOperationStatus.Accepted || other.Status == AzureProviderOperationStatus.Queued ||
                          other.Status == AzureProviderOperationStatus.Running || other.Status == AzureProviderOperationStatus.RecoveryRequired)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AzureProviderOperationStatus.Running)
                    .SetProperty(x => x.WorkerId, workerId)
                    .SetProperty(x => x.LeaseTokenHash, hash)
                    .SetProperty(x => x.CompletionLeaseTokenHash, (string?)null)
                    .SetProperty(x => x.CompletionFingerprint, (string?)null)
                    .SetProperty(x => x.LeaseExpiresAt, leaseExpires)
                    .SetProperty(x => x.HeartbeatAt, now)
                    .SetProperty(x => x.AttemptNumber, x => x.AttemptNumber + 1)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (changed == 0)
            {
                var replay = await db.AzureProviderOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.WorkspaceId == workspaceId && x.Id == operationId &&
                    x.Status == AzureProviderOperationStatus.Running && x.WorkerId == workerId &&
                    x.LeaseTokenHash == hash && x.LeaseExpiresAt > now, cancellationToken);
                result = replay is null ? null : ToModel(replay);
                return;
            }
            db.ChangeTracker.Clear();
            var entity = await db.AzureProviderOperations.SingleAsync(x => x.Id == operationId, cancellationToken);
            AddTransition(entity, allowRecovery ? "operation.recovery.claimed" : "operation.claimed", allowRecovery ? "Recovery reconciliation claimed." : "Azure provider operation claimed.", now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = ToModel(entity);
        });
        return result;
    }

    public async Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        AzureProviderOperationValidation.ValidateLeaseToken(leaseToken);
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1)) throw new ArgumentException("Lease duration must be positive and bounded.");
        DateTimeOffset leaseExpires;
        try { leaseExpires = now.Add(leaseDuration); } catch (ArgumentOutOfRangeException) { throw new ArgumentException("Lease duration overflowed.", nameof(leaseDuration)); }
        var entity = await db.AzureProviderOperations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken);
        if (entity is null || entity.Status != AzureProviderOperationStatus.Running || !LeaseMatches(entity, leaseToken, now) || expectedVersion.HasValue && entity.Version != expectedVersion.Value) return null;
        entity.HeartbeatAt = now; entity.LeaseExpiresAt = leaseExpires; entity.UpdatedAt = now; entity.Version++;
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
        return ToModel(entity);
    }

    public async Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        AzureProviderOperationValidation.ValidateLeaseToken(leaseToken);
        AzureProviderOperationValidation.ValidateCheckpoint(checkpoint);
        var entity = await db.AzureProviderOperations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken);
        if (entity is null || entity.Status != AzureProviderOperationStatus.Running || !LeaseMatches(entity, leaseToken, now) || expectedVersion.HasValue && entity.Version != expectedVersion.Value) return null;
        if ((long)checkpoint.Phase < (long)entity.Phase) throw new InvalidOperationException("Checkpoint phase cannot move backwards.");
        var safeDiagnostics = checkpoint.Diagnostics
            .Select(x => new AzureProviderDiagnostic(x.Code, x.Code))
            .ToArray();
        var diagnosticsJson = JsonSerializer.Serialize(safeDiagnostics);
        var resources = checkpoint.ReplaceResources
            ? checkpoint.Resources
            : MergeResources(entity, checkpoint.Resources);
        var lastTransitionCode = await db.AzureProviderOperationTransitions.AsNoTracking()
            .Where(x => x.OperationId == entity.Id)
            .OrderByDescending(x => x.Sequence)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);
        var endpoint = checkpoint.Endpoint ?? entity.Endpoint;
        var health = checkpoint.Health == AzureProviderHealth.Unknown ? entity.Health : checkpoint.Health;
        if (entity.Phase == checkpoint.Phase && entity.Endpoint == endpoint && entity.Health == health &&
            entity.DiagnosticsJson == diagnosticsJson && ResourcesEqual(entity, resources) &&
            lastTransitionCode == checkpoint.Code)
            return ToModel(entity);
        entity.Phase = checkpoint.Phase; entity.CheckpointSequence++; entity.Version++; entity.UpdatedAt = now;
        entity.ResourceGroupName = resources.ResourceGroupName; entity.FoundationDeploymentId = resources.FoundationDeploymentId;
        entity.WorkloadDeploymentId = resources.WorkloadDeploymentId; entity.WorkloadResourceId = resources.WorkloadResourceId;
        entity.WorkloadRevisionName = resources.WorkloadRevisionName; entity.StableTrafficRevisionName = resources.StableTrafficRevisionName;
        entity.Endpoint = endpoint; entity.Health = health;
        entity.DiagnosticsJson = diagnosticsJson;
        AddTransition(entity, checkpoint.Code, checkpoint.Code, now);
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
        return ToModel(entity);
    }

    public async Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        AzureProviderOperationValidation.ValidateLeaseToken(leaseToken);
        AzureProviderOperationValidation.ValidateCode(code);
        if (status is not (AzureProviderOperationStatus.Succeeded or AzureProviderOperationStatus.Failed or AzureProviderOperationStatus.Cancelled or AzureProviderOperationStatus.RecoveryRequired))
            throw new ArgumentException("Invalid final operation status.", nameof(status));
        var entity = await db.AzureProviderOperations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken);
        if (entity is null) return null;
        var completionFingerprint = Hash($"{status}|{code}");
        if (entity.Status == status) return entity.CompletionLeaseTokenHash == Hash(leaseToken) && entity.CompletionFingerprint == completionFingerprint ? ToModel(entity) : null;
        if (entity.Status != AzureProviderOperationStatus.Running || !LeaseMatches(entity, leaseToken, now) || expectedVersion.HasValue && entity.Version != expectedVersion.Value) return null;
        entity.Status = status; entity.UpdatedAt = now; entity.CompletedAt = status == AzureProviderOperationStatus.RecoveryRequired ? null : now; entity.Version++;
        entity.CompletionLeaseTokenHash = entity.LeaseTokenHash;
        entity.CompletionFingerprint = completionFingerprint;
        entity.LeaseTokenHash = null; entity.LeaseExpiresAt = null; entity.WorkerId = null;
        AddTransition(entity, code, code, now);
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
        return ToModel(entity);
    }

    public async Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var candidates = await db.AzureProviderOperations.AsNoTracking()
                .Where(x => x.Status == AzureProviderOperationStatus.Running && x.LeaseExpiresAt != null && x.LeaseExpiresAt <= now)
                .ToListAsync(cancellationToken);
            var recovered = 0;
            foreach (var candidate in candidates)
            {
                var changed = await db.AzureProviderOperations.Where(x => x.Id == candidate.Id && x.Status == AzureProviderOperationStatus.Running && x.Version == candidate.Version && x.LeaseExpiresAt != null && x.LeaseExpiresAt <= now)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, AzureProviderOperationStatus.RecoveryRequired)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1)
                        .SetProperty(x => x.LeaseTokenHash, (string?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                        .SetProperty(x => x.WorkerId, (string?)null)
                        .SetProperty(x => x.CompletionLeaseTokenHash, (string?)null)
                        .SetProperty(x => x.CompletionFingerprint, (string?)null), cancellationToken);
                if (changed == 0) continue;
                recovered++;
                candidate.Status = AzureProviderOperationStatus.RecoveryRequired;
                candidate.Version++;
                AddTransition(candidate, "operation.recovery.required", "The operation lease expired before completion.", now);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return recovered;
        });
    }

    public async Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!await db.AzureProviderOperations.AsNoTracking()
                .AnyAsync(x => x.Id == operationId && x.WorkspaceId == workspaceId, cancellationToken))
            return [];

        return (await db.AzureProviderOperationTransitions.AsNoTracking()
                .Where(x => x.OperationId == operationId)
                .OrderBy(x => x.Sequence)
                .ToListAsync(cancellationToken))
            .Select(ToTransition)
            .ToList();
    }

    private async Task<AzureProviderOperation?> FindByKeyAsync(AzureProviderOperationRequest request, CancellationToken cancellationToken) =>
        await db.AzureProviderOperations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == request.WorkspaceId && x.TargetKey == request.TargetKey && x.IdempotencyKey == request.IdempotencyKey, cancellationToken) is { } entity ? ToModel(entity) : null;

    private async Task<AzureProviderOperationEntity?> FindActiveTargetAsync(
        AzureProviderOperationRequest request,
        CancellationToken cancellationToken) =>
        await db.AzureProviderOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == request.WorkspaceId && x.TargetKey == request.TargetKey &&
                        (x.Status == AzureProviderOperationStatus.Accepted || x.Status == AzureProviderOperationStatus.Queued ||
                         x.Status == AzureProviderOperationStatus.Running || x.Status == AzureProviderOperationStatus.RecoveryRequired))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static AzureProviderOperation EnsureSameRequest(AzureProviderOperation operation, string hash) =>
        operation.RequestHash == hash ? operation : throw new InvalidOperationException("The idempotency key is already bound to a different request.");

    private static bool LeaseMatches(AzureProviderOperationEntity entity, string token, DateTimeOffset now) =>
        entity.LeaseTokenHash == Hash(token) && entity.LeaseExpiresAt > now;

    private void AddTransition(AzureProviderOperationEntity entity, string code, string message, DateTimeOffset now)
    {
        db.AzureProviderOperationTransitions.Add(new AzureProviderOperationTransitionEntity
        {
            Id = Guid.NewGuid(),
            OperationId = entity.Id,
            Sequence = entity.Version,
            Status = entity.Status,
            Phase = entity.Phase,
            Code = code,
            Message = message,
            OccurredAt = now
        });
    }

    private static bool ResourcesEqual(AzureProviderOperationEntity entity, AzureProviderResourceReferences resources) =>
        entity.ResourceGroupName == resources.ResourceGroupName && entity.FoundationDeploymentId == resources.FoundationDeploymentId &&
        entity.WorkloadDeploymentId == resources.WorkloadDeploymentId && entity.WorkloadResourceId == resources.WorkloadResourceId &&
        entity.WorkloadRevisionName == resources.WorkloadRevisionName && entity.StableTrafficRevisionName == resources.StableTrafficRevisionName;

    private static AzureProviderResourceReferences MergeResources(
        AzureProviderOperationEntity entity,
        AzureProviderResourceReferences incoming) =>
        new(
            incoming.ResourceGroupName ?? entity.ResourceGroupName,
            incoming.FoundationDeploymentId ?? entity.FoundationDeploymentId,
            incoming.WorkloadDeploymentId ?? entity.WorkloadDeploymentId,
            incoming.WorkloadResourceId ?? entity.WorkloadResourceId,
            incoming.WorkloadRevisionName ?? entity.WorkloadRevisionName,
            incoming.StableTrafficRevisionName ?? entity.StableTrafficRevisionName);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static AzureProviderOperation ToModel(AzureProviderOperationEntity x)
    {
        var (diagnostics, diagnosticsInvalid) = ReadDiagnostics(x.DiagnosticsJson);
        var (secretReferences, secretReferencesInvalid) = ReadSecretReferences(x.SecretReferencesJson);
        return new(
            x.Id, x.WorkspaceId, x.TargetKey, x.Action, x.IdempotencyKey, x.RequestHash, x.OperationIdentity,
            x.PlanFingerprint, x.TemplateFingerprint, x.ElsaVersion, x.ReleaseLine, x.Topology, x.Isolation,
            x.Location, x.ImageRepository, x.ImageDigest, x.ReleaseManifestDigest, x.ReleaseManifestSignatureDigest,
            x.Status, x.Phase, x.CheckpointSequence, x.AttemptNumber, x.Version,
            new(x.ResourceGroupName, x.FoundationDeploymentId, x.WorkloadDeploymentId, x.WorkloadResourceId, x.WorkloadRevisionName, x.StableTrafficRevisionName),
            x.Endpoint, x.Health, diagnostics,
            x.WorkerId, x.LeaseExpiresAt, x.HeartbeatAt, x.CreatedAt, x.UpdatedAt, x.CompletedAt,
            x.ReleaseManifestReference, x.ReleaseManifestSignatureReference,
            secretReferences,
            diagnosticsInvalid || secretReferencesInvalid);
    }

    private static (IReadOnlyList<AzureProviderDiagnostic> Diagnostics, bool Invalid) ReadDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ([], true);

        try
        {
            var diagnostics = JsonSerializer.Deserialize<List<AzureProviderDiagnostic>>(json);
            if (diagnostics is null || !AzureProviderOperationValidation.IsSafeDiagnostics(diagnostics))
                return ([], true);

            return (diagnostics.Select(x => new AzureProviderDiagnostic(x.Code, x.Message)).ToArray(), false);
        }
        catch (JsonException)
        {
            return ([], true);
        }
        catch (NotSupportedException)
        {
            return ([], true);
        }
    }

    private static (IReadOnlyDictionary<string, string> SecretReferences, bool Invalid) ReadSecretReferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (EmptySecretReferences, true);

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (values is null)
                return (EmptySecretReferences, true);

            var normalizedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                if (pair.Key is null || pair.Value is null ||
                    !string.Equals(pair.Key, pair.Key.Trim().ToLowerInvariant(), StringComparison.Ordinal) ||
                    !normalizedKeys.Add(pair.Key.Trim()) ||
                    !AzureProviderOperationValidation.IsSafeSecretReference(pair.Value))
                    return (EmptySecretReferences, true);

                references.Add(pair.Key, pair.Value);
            }

            return (new ReadOnlyDictionary<string, string>(references), false);
        }
        catch (JsonException)
        {
            return (EmptySecretReferences, true);
        }
        catch (NotSupportedException)
        {
            return (EmptySecretReferences, true);
        }
    }

    private static AzureProviderOperationTransition ToTransition(AzureProviderOperationTransitionEntity x) => new(x.Id, x.OperationId, x.Sequence, x.Status, x.Phase, x.Code, x.Message, x.OccurredAt);
}
