using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactDesiredStateReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactDigest",
                table: "StructuredDesiredStateRecords",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactDigestAlgorithm",
                table: "StructuredDesiredStateRecords",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactId",
                table: "StructuredDesiredStateRecords",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArtifactRecordId",
                table: "StructuredDesiredStateRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactTypeId",
                table: "StructuredDesiredStateRecords",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE StructuredDesiredStateRecords
                SET ArtifactRecordId = NULLIF(json_extract(PayloadJson, '$.artifactRecordId'), ''),
                    ArtifactId = NULLIF(json_extract(PayloadJson, '$.artifactId'), ''),
                    ArtifactTypeId = NULLIF(json_extract(PayloadJson, '$.artifactTypeId'), ''),
                    ArtifactDigestAlgorithm = NULLIF(json_extract(PayloadJson, '$.contentDigest.algorithm'), ''),
                    ArtifactDigest = NULLIF(json_extract(PayloadJson, '$.contentDigest.value'), '')
                WHERE Kind = 'ArtifactReference'
                  AND json_valid(PayloadJson);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StructuredDesiredStateRecords_WorkspaceId_ArtifactId",
                table: "StructuredDesiredStateRecords",
                columns: new[] { "WorkspaceId", "ArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_StructuredDesiredStateRecords_WorkspaceId_ArtifactRecordId",
                table: "StructuredDesiredStateRecords",
                columns: new[] { "WorkspaceId", "ArtifactRecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StructuredDesiredStateRecords_WorkspaceId_ArtifactId",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropIndex(
                name: "IX_StructuredDesiredStateRecords_WorkspaceId_ArtifactRecordId",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropColumn(
                name: "ArtifactDigest",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropColumn(
                name: "ArtifactDigestAlgorithm",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropColumn(
                name: "ArtifactId",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropColumn(
                name: "ArtifactRecordId",
                table: "StructuredDesiredStateRecords");

            migrationBuilder.DropColumn(
                name: "ArtifactTypeId",
                table: "StructuredDesiredStateRecords");
        }
    }
}
