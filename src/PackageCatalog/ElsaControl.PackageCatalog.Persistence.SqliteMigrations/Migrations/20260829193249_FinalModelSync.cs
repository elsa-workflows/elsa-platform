using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class FinalModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ElsaInstanceOperations_NullInstanceOnlyCreate",
                table: "ElsaInstanceOperations",
                sql: "InstanceId IS NOT NULL OR Action = 'Create'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ElsaInstanceOperations_NullInstanceOnlyCreate",
                table: "ElsaInstanceOperations");
        }
    }
}
