using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

internal sealed class HealingPersistenceFixture(SqliteConnection connection, HealingDbContext db) : IAsyncDisposable
{
    public HealingDbContext Db { get; } = db;

    public static async Task<HealingPersistenceFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HealingDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new HealingPersistenceFixture(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
