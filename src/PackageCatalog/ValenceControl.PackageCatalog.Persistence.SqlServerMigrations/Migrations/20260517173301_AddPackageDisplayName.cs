using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Packages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Strips the "Elsa." prefix (5 chars). Keep in sync with PackageDisplayNamePolicy.ElsaPackagePrefix.
            migrationBuilder.Sql(
                """
                UPDATE Packages
                SET DisplayName = CASE
                    WHEN LOWER(LEFT(PackageId, 5)) = 'elsa.' THEN SUBSTRING(PackageId, 6, LEN(PackageId))
                    ELSE PackageId
                END
                WHERE DisplayName = ''
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Packages");
        }
    }
}
