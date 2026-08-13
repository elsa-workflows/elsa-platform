using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore;

/// <summary>
/// Serializes repair admission for an application on its durable configuration row. The write lock is held
/// until the caller's transaction completes, so it coordinates all control instances on SQLite and SQL Server.
/// </summary>
public static class HealingRepairAdmission
{
    public static async ValueTask<bool> AcquireApplicationLockAsync(
        HealingDbContext dbContext,
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Repair admission must be acquired inside a database transaction.");
        var affected = await dbContext.HealingConfigurations
            .Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, x => x.UpdatedAt), cancellationToken);
        return affected == 1;
    }
}
