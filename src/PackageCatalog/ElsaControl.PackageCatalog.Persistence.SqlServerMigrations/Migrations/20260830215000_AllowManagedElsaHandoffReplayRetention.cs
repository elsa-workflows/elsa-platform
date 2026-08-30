using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260830215000_AllowManagedElsaHandoffReplayRetention")]
public sealed class AllowManagedElsaHandoffReplayRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffReplayConsumptions_AppendOnly;");
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER TR_ManagedElsaHandoffReplayConsumptions_AppendOnly
            ON ManagedElsaHandoffReplayConsumptions
            INSTEAD OF UPDATE
            AS BEGIN
                THROW 51010, ''Managed Elsa handoff replay records are append-only'', 1;
            END;');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffReplayConsumptions_AppendOnly;");
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER TR_ManagedElsaHandoffReplayConsumptions_AppendOnly
            ON ManagedElsaHandoffReplayConsumptions
            INSTEAD OF UPDATE, DELETE
            AS BEGIN
                THROW 51010, ''Managed Elsa handoff replay records are append-only'', 1;
            END;');
            """);
    }
}
