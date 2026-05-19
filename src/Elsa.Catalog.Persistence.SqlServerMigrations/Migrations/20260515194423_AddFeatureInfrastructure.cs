using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Catalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InfrastructureJson",
                table: "Features",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InfrastructureJson",
                table: "Features");
        }
    }
}
