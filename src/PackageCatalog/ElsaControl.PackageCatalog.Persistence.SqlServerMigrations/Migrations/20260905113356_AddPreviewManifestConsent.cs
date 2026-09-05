using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewManifestConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviewManifestDigest",
                table: "ElsaInstances",
                type: "nvarchar(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewManifestDigest",
                table: "ElsaInstanceIntentRevisions",
                type: "nvarchar(71)",
                maxLength: 71,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewManifestDigest",
                table: "ElsaInstances");

            migrationBuilder.DropColumn(
                name: "PreviewManifestDigest",
                table: "ElsaInstanceIntentRevisions");
        }
    }
}
