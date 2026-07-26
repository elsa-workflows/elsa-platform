using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ValenceControl.Healing.Persistence.SqliteMigrations;

public sealed class HealingSqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HealingDbContext>
{
    public HealingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite("Data Source=valence-control-healing.design.db", sqlite =>
            {
                sqlite.MigrationsAssembly(typeof(HealingSqliteDesignTimeDbContextFactory).Assembly.GetName().Name);
                sqlite.MigrationsHistoryTable(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable);
            })
            .Options;
        return new HealingDbContext(options);
    }
}
