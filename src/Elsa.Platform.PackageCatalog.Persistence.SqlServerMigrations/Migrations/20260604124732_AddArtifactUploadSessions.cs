using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    UploadedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    StagedFilePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedArtifactRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
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
