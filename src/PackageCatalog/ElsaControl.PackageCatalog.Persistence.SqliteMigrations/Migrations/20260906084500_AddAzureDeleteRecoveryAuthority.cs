using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260906084500_AddAzureDeleteRecoveryAuthority")]
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
        migrationBuilder.DropColumn(
            name: "AzureDeleteRecoveryAuthority",
            table: "ElsaInstanceRecoveryRequests");
    }
}
