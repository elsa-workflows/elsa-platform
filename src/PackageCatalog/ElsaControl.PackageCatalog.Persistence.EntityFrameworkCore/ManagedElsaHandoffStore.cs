using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Persistence-neutral handoff metadata accepted by the EF adapter. The API
/// project maps its handoff contract to this record without making persistence
/// depend on the hosting layer.
/// </summary>
public sealed record ManagedElsaHandoffAuditRecord(
    string Action,
    string Jti,
    Guid? AccountId,
    Guid? OrganizationId,
    Guid? InstanceId,
    string? Audience,
    int? BindingVersion,
    string? CorrelationId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Relational storage for one-time handoff consumption and safe audit metadata.
/// </summary>
public sealed class EfCoreManagedElsaHandoffStore(
    CatalogDbContext dbContext,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Consumes a JTI exactly once. The unique primary key insert is the atomic
    /// cross-process boundary; a duplicate key means another consumer won.
    /// </summary>
    public async ValueTask<bool> TryConsumeAsync(
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);

        dbContext.ChangeTracker.Clear();
        if (await dbContext.ManagedElsaHandoffReplays.AsNoTracking()
                .AnyAsync(x => x.Jti == jti, cancellationToken))
            return false;

        var entity = new ManagedElsaHandoffReplayEntity
        {
            Jti = jti,
            ExpiresAt = expiresAt.ToUniversalTime(),
            ConsumedAt = _timeProvider.GetUtcNow().ToUniversalTime()
        };
        dbContext.ManagedElsaHandoffReplays.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (EfCoreDatabaseExceptionPolicy.IsUniqueViolation(exception))
        {
            // A duplicate can leave the attempted entity tracked as Added. Clear
            // it so the scoped context remains usable after a replay race.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask RecordAsync(
        ManagedElsaHandoffAuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        dbContext.ManagedElsaHandoffAuditEvents.Add(new ManagedElsaHandoffAuditEventEntity
        {
            Id = Guid.NewGuid(),
            Action = audit.Action,
            Jti = audit.Jti,
            AccountId = audit.AccountId,
            OrganizationId = audit.OrganizationId,
            InstanceId = audit.InstanceId,
            Audience = audit.Audience,
            BindingVersion = audit.BindingVersion,
            CorrelationId = audit.CorrelationId,
            OccurredAt = audit.OccurredAt.ToUniversalTime()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

}

internal static class EfCoreDatabaseExceptionPolicy
{
    internal static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 19 })
                return true;
            if (current is SqlException { Number: 2601 or 2627 })
                return true;
        }

        return false;
    }
}
