using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderPackageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SqlQuartzPackageVersion",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SqlWorkflowPackageVersion",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SqlQuartzPackageVersion", table: "AzureProviderOperations");
            migrationBuilder.DropColumn(name: "SqlWorkflowPackageVersion", table: "AzureProviderOperations");
        }
    }
}
