using System.Data;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore;

internal static class HealingPersistenceTransaction
{
    public static async ValueTask<T> ExecuteAsync<T>(
        HealingDbContext dbContext,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(operation);
        if (dbContext.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
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
}
