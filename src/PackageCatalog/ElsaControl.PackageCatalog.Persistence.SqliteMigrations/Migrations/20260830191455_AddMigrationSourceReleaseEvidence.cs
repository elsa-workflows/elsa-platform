using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
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
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseEvidenceReference",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseProviderCorrelationId",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;
                CREATE TRIGGER TR_ElsaInstanceMigrations_NoDelete
                BEFORE DELETE ON ElsaInstanceMigrations
                BEGIN SELECT RAISE(ABORT, 'Elsa instance migrations are append-only.'); END;
                """);
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
