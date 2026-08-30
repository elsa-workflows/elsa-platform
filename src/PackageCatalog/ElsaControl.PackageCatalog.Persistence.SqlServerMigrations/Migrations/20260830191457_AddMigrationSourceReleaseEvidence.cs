using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationSourceReleaseEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseEvidenceDigest",
                table: "ElsaInstanceMigrations",
                type: "nvarchar(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseEvidenceReference",
                table: "ElsaInstanceMigrations",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseProviderCorrelationId",
                table: "ElsaInstanceMigrations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceReleaseEvidenceDigest",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseEvidenceReference",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseProviderCorrelationId",
                table: "ElsaInstanceMigrations");
        }
    }
}
