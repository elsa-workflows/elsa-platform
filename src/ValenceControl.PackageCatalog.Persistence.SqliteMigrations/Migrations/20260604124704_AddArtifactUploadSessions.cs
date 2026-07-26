using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactUploadSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceArtifactUploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DeclaredSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    UploadedSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    StagedFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedArtifactRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceArtifactUploadSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceArtifactUploadSessions_WorkspaceId_IdempotencyKey",
                table: "WorkspaceArtifactUploadSessions",
                columns: new[] { "WorkspaceId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceArtifactUploadSessions_WorkspaceId_Status_ExpiresAt",
                table: "WorkspaceArtifactUploadSessions",
                columns: new[] { "WorkspaceId", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceArtifactUploadSessions");
        }
    }
}
