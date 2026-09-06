using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations;

public partial class AddAzureDeleteRecoveryAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AzureDeleteRecoveryAuthority",
            table: "ElsaInstanceRecoveryRequests",
            type: "nvarchar(max)",
            maxLength: 4096,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AzureDeleteRecoveryAuthority",
            table: "ElsaInstanceRecoveryRequests");
    }
}
