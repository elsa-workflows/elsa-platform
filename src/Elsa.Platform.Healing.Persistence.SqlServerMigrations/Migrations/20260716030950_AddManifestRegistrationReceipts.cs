using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddManifestRegistrationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealingComponentManifestRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifestRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentManifestRegistrations_HealingComponentManifests_WorkspaceId_ApplicationId_ManifestId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId },
                        principalTable: "HealingComponentManifests",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestRegistrations_WorkspaceId_ApplicationId_ManifestId",
                table: "HealingComponentManifestRegistrations",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestRegistrations_WorkspaceId_ApplicationId_RevisionId_IdempotencyKey",
                table: "HealingComponentManifestRegistrations",
                columns: new[] { "WorkspaceId", "ApplicationId", "RevisionId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingComponentManifestRegistrations");
        }
    }
}
