using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
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
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Strips the "Elsa." prefix (5 chars). Keep in sync with PackageDisplayNamePolicy.ElsaPackagePrefix.
            migrationBuilder.Sql(
                """
                UPDATE Packages
                SET DisplayName = CASE
                    WHEN lower(substr(PackageId, 1, 5)) = 'elsa.' THEN substr(PackageId, 6)
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
