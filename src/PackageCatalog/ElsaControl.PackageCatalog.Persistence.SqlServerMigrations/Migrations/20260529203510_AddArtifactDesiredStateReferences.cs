using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactDigestAlgorithm",
                table: "StructuredDesiredStateRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactId",
                table: "StructuredDesiredStateRecords",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArtifactRecordId",
                table: "StructuredDesiredStateRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactTypeId",
                table: "StructuredDesiredStateRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE StructuredDesiredStateRecords
                SET ArtifactRecordId = TRY_CONVERT(uniqueidentifier, JSON_VALUE(PayloadJson, '$.artifactRecordId')),
                    ArtifactId = NULLIF(JSON_VALUE(PayloadJson, '$.artifactId'), ''),
                    ArtifactTypeId = NULLIF(JSON_VALUE(PayloadJson, '$.artifactTypeId'), ''),
                    ArtifactDigestAlgorithm = NULLIF(JSON_VALUE(PayloadJson, '$.contentDigest.algorithm'), ''),
                    ArtifactDigest = NULLIF(JSON_VALUE(PayloadJson, '$.contentDigest.value'), '')
                WHERE Kind = 'ArtifactReference'
                  AND ISJSON(PayloadJson) = 1;
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
