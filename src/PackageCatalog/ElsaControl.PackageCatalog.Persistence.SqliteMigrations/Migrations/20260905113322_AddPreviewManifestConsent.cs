using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
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
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewManifestDigest",
                table: "ElsaInstanceIntentRevisions",
                type: "TEXT",
                maxLength: 71,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite 3.35+ supports native DROP COLUMN. Using it here keeps
            // native database triggers intact; the provider's table-rebuild
            // fallback can otherwise invalidate triggers referencing ElsaInstances.
            migrationBuilder.Sql("ALTER TABLE ElsaInstances DROP COLUMN PreviewManifestDigest;");
            migrationBuilder.Sql("ALTER TABLE ElsaInstanceIntentRevisions DROP COLUMN PreviewManifestDigest;");
        }
    }
}
