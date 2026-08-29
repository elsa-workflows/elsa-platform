using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderOperationPlanMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleaseManifestReference",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseManifestSignatureReference",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecretReferencesJson",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 10000,
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseManifestReference",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "ReleaseManifestSignatureReference",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "SecretReferencesJson",
                table: "AzureProviderOperations");
        }
    }
}
