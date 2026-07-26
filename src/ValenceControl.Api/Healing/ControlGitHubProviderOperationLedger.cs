using System.Security.Cryptography;
using System.Text;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Healing;

/// <summary>
/// Persists provider-side idempotency receipts separately from the leased provider-operation outbox.
/// This prevents an executing outbox handler from colliding with its own remote-mutation reservation.
/// </summary>
public sealed class ControlGitHubProviderOperationLedger(
    HealingDbContext dbContext) : IGitHubProviderOperationLedger
{
    public async ValueTask<GitHubProviderOperationRecord?> GetAsync(
        GitHubProviderOperationKey key,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindAsync(key, cancellationToken);
        return entry is null ? null : ToRecord(entry, key);
    }

    public async ValueTask<bool> TryReserveAsync(
        GitHubProviderOperationKey key,
        string canonicalPayloadJson,
        string payloadHash,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken = default)
    {
        var provider = await dbContext.ProviderConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == key.ProviderConnectionId && x.Status == ProviderConnectionStatus.Active,
            cancellationToken);
        if (provider is null)
            return false;
        var existing = await dbContext.ProviderMutationJournalEntries.SingleOrDefaultAsync(
            x => x.ProviderConnectionId == key.ProviderConnectionId &&
                 x.Kind == key.Kind &&
                 x.IdempotencyKey == key.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Completed ||
                existing.UpdatedAt.Add(GitHubProviderOperationDefaults.ReservationLifetime) > reservedAt ||
                !FixedEquals(existing.PayloadHash, payloadHash) ||
                !FixedEquals(existing.SafePayloadJson, canonicalPayloadJson))
                return false;
            existing.UpdatedAt = reservedAt;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.Entry(existing).State = EntityState.Detached;
                return false;
            }
        }

        var entry = new ProviderMutationJournalEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = provider.WorkspaceId,
            ProviderConnectionId = provider.Id,
            Kind = key.Kind,
            IdempotencyKey = key.IdempotencyKey,
            SafePayloadJson = canonicalPayloadJson,
            PayloadHash = payloadHash,
            Completed = false,
            CreatedAt = reservedAt,
            UpdatedAt = reservedAt
        };
        dbContext.ProviderMutationJournalEntries.Add(entry);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            if (await FindAsync(key, cancellationToken) is not null)
                return false;
            throw;
        }
    }

    public async ValueTask CompleteAsync(
        GitHubProviderOperationKey key,
        string canonicalPayloadJson,
        string payloadHash,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.ProviderMutationJournalEntries.SingleOrDefaultAsync(
            x => x.ProviderConnectionId == key.ProviderConnectionId &&
                 x.Kind == key.Kind &&
                 x.IdempotencyKey == key.IdempotencyKey,
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.OperationInProgress);
        if (!FixedEquals(entry.PayloadHash, payloadHash) ||
            !FixedEquals(entry.SafePayloadJson, canonicalPayloadJson))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
        if (entry.Completed)
        {
            if (!FixedEquals(entry.ResultJson ?? string.Empty, resultJson))
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
            return;
        }
        entry.Completed = true;
        entry.ResultJson = resultJson;
        entry.UpdatedAt = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private ValueTask<ProviderMutationJournalEntry?> FindAsync(
        GitHubProviderOperationKey key,
        CancellationToken cancellationToken) =>
        new(dbContext.ProviderMutationJournalEntries.AsNoTracking().SingleOrDefaultAsync(
            x => x.ProviderConnectionId == key.ProviderConnectionId &&
                 x.Kind == key.Kind &&
                 x.IdempotencyKey == key.IdempotencyKey,
            cancellationToken));

    private static GitHubProviderOperationRecord ToRecord(
        ProviderMutationJournalEntry entry,
        GitHubProviderOperationKey key) => new(
        key,
        entry.SafePayloadJson,
        entry.PayloadHash,
        entry.Completed
            ? GitHubProviderOperationStatus.Completed
            : GitHubProviderOperationStatus.Reserved,
        entry.ResultJson,
        entry.UpdatedAt);

    private static bool FixedEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}
