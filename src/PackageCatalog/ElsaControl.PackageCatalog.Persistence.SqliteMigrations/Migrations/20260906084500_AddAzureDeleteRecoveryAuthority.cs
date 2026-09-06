using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations;

public partial class AddAzureDeleteRecoveryAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AzureDeleteRecoveryAuthority",
            table: "ElsaInstanceRecoveryRequests",
            type: "TEXT",
            maxLength: 4096,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Use the supported native drop, as in preview-consent rollback, to preserve the
        // recovery ledger and its append-only triggers instead of rebuilding the table.
        migrationBuilder.Sql("ALTER TABLE ElsaInstanceRecoveryRequests DROP COLUMN AzureDeleteRecoveryAuthority;");
    }
}
