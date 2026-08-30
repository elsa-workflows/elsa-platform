using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260830215000_AllowManagedElsaHandoffReplayRetention")]
public sealed class AllowManagedElsaHandoffReplayRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffReplayConsumptions_AppendOnly_Delete;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TRIGGER TR_ManagedElsaHandoffReplayConsumptions_AppendOnly_Delete
            BEFORE DELETE ON ManagedElsaHandoffReplayConsumptions
            BEGIN SELECT RAISE(ABORT, 'Managed Elsa handoff replay records are append-only'); END;
            """);
}
