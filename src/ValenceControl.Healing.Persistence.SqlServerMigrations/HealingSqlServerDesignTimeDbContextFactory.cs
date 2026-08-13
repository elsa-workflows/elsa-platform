using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ValenceControl.Healing.Persistence.SqlServerMigrations;

public sealed class HealingSqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HealingDbContext>
{
    public HealingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=ValenceControlHealingDesign;User ID=design;Password=not-used;Encrypt=False",
                sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(HealingSqlServerDesignTimeDbContextFactory).Assembly.GetName().Name);
                    sqlServer.MigrationsHistoryTable(HealingDatabaseServiceCollectionExtensions.MigrationsHistoryTable);
                })
            .Options;
        return new HealingDbContext(options);
    }
}
