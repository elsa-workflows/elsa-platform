using System.Data;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Resolves the current managed Elsa identity binding from the instance aggregate.
/// A missing, deleted, unavailable or malformed binding is represented as null so
/// callers cannot accidentally construct a fallback audience or callback.
/// </summary>
public sealed class EfCoreManagedElsaInstanceIdentityStore(CatalogDbContext dbContext) : IManagedElsaInstanceIdentityStore
{
    public async Task<ManagedElsaInstanceIdentity?> FindAsync(
        Guid organizationId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || instanceId == Guid.Empty)
            return null;

        dbContext.ChangeTracker.Clear();
        var entity = await dbContext.ElsaInstances
            .AsNoTracking()
            .Include(x => x.IdentityBinding)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == instanceId,
                cancellationToken);
        return entity is null ? null : TryMap(entity);
    }

    /// <summary>
    /// Creates a binding when no expected version is supplied, or rotates the
    /// current binding when the supplied version matches. Both operations repeat
    /// ownership and lifecycle checks inside a serializable transaction.
    /// </summary>
    public async Task<ManagedElsaInstanceIdentityBindingWriteResult> BindAsync(
        Guid organizationId,
        Guid workspaceId,
        Guid instanceId,
        string verifiedEndpointOrigin,
        int? expectedBindingVersion,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || workspaceId == Guid.Empty || instanceId == Guid.Empty)
            return NotFound();
        if (expectedBindingVersion is <= 0)
            return Conflict();

        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var entity = await dbContext.ElsaInstances
            .Include(x => x.IdentityBinding)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.WorkspaceId == workspaceId && x.Id == instanceId,
                cancellationToken);

        if (entity is null || IsUnavailable(entity))
            return NotFound();

        var existing = entity.IdentityBinding;
        ElsaInstanceIdentityBinding binding;
        ManagedElsaInstanceIdentityBindingWriteOutcome outcome;
        try
        {
            if (expectedBindingVersion is null)
            {
                if (existing is not null)
                    return Conflict();

                binding = ElsaInstanceIdentityBinding.Create(instanceId, verifiedEndpointOrigin, changedAt);
                entity.IdentityBinding = new ElsaInstanceIdentityBindingEntity
                {
                    InstanceId = instanceId,
                    Audience = binding.Audience,
                    CanonicalCallbackUri = binding.CanonicalCallbackUri,
                    VerifiedEndpointOrigin = binding.VerifiedEndpointOrigin,
                    BindingVersion = binding.BindingVersion,
                    ChangedAt = binding.ChangedAt
                };
                outcome = ManagedElsaInstanceIdentityBindingWriteOutcome.Created;
            }
            else
            {
                if (existing is null)
                    return NotFound();
                if (existing.BindingVersion != expectedBindingVersion)
                    return Conflict();

                var current = ElsaInstanceIdentityBinding.Hydrate(
                    instanceId,
                    existing.VerifiedEndpointOrigin,
                    existing.BindingVersion,
                    existing.ChangedAt);
                binding = current.Rotate(verifiedEndpointOrigin, changedAt);
                existing.Audience = binding.Audience;
                existing.CanonicalCallbackUri = binding.CanonicalCallbackUri;
                existing.VerifiedEndpointOrigin = binding.VerifiedEndpointOrigin;
                existing.BindingVersion = binding.BindingVersion;
                existing.ChangedAt = binding.ChangedAt;
                outcome = ManagedElsaInstanceIdentityBindingWriteOutcome.Rotated;
            }
        }
        catch (ArgumentException)
        {
            return Conflict();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(outcome, Map(entity, binding));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Conflict();
        }
        catch (DbUpdateException exception) when (EfCoreDatabaseExceptionPolicy.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Conflict();
        }
    }

    private static ManagedElsaInstanceIdentity? TryMap(ElsaInstanceEntity entity)
    {
        if (IsUnavailable(entity) || entity.IdentityBinding is null)
            return null;

        var persisted = entity.IdentityBinding;
        try
        {
            var binding = ElsaInstanceIdentityBinding.Hydrate(
                entity.Id,
                persisted.VerifiedEndpointOrigin,
                persisted.BindingVersion,
                persisted.ChangedAt);
            if (!string.Equals(persisted.Audience, binding.Audience, StringComparison.Ordinal) ||
                !string.Equals(persisted.CanonicalCallbackUri, binding.CanonicalCallbackUri, StringComparison.Ordinal) ||
                !Uri.TryCreate(binding.CanonicalCallbackUri, UriKind.Absolute, out var callbackUri))
                return null;

            return Map(entity, binding, callbackUri);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ManagedElsaInstanceIdentity Map(
        ElsaInstanceEntity entity,
        ElsaInstanceIdentityBinding binding,
        Uri? callbackUri = null) =>
        new(
            entity.OrganizationId,
            entity.WorkspaceId,
            entity.Id,
            binding.Audience,
            callbackUri ?? new Uri(binding.CanonicalCallbackUri, UriKind.Absolute),
            binding.BindingVersion);

    private static bool IsUnavailable(ElsaInstanceEntity entity) =>
        entity.DeletedAt is not null ||
        entity.DesiredLifecycle == ElsaDesiredLifecycle.Deleting ||
        entity.ObservedLifecycle is ElsaObservedLifecycle.Deleting or ElsaObservedLifecycle.Deleted;

    private static ManagedElsaInstanceIdentityBindingWriteResult NotFound() =>
        new(ManagedElsaInstanceIdentityBindingWriteOutcome.NotFound, null);

    private static ManagedElsaInstanceIdentityBindingWriteResult Conflict() =>
        new(ManagedElsaInstanceIdentityBindingWriteOutcome.Conflict, null);

}
