using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddComponentManifestAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId",
                table: "HealingComponentManifestEntries");

            migrationBuilder.AlterColumn<string>(
                name: "RelativePath",
                table: "HealingComponentManifestEntries",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AddColumn<string>(
                name: "KindName",
                table: "HealingComponentManifestEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE [HealingComponentManifestEntries]
                SET [KindName] = CASE [Kind] WHEN 0 THEN 'application' WHEN 1 THEN 'package' WHEN 2 THEN 'assembly' ELSE 'unknown' END,
                    [Kind] = CASE [Kind] WHEN 0 THEN 1 WHEN 1 THEN 2 WHEN 2 THEN 3 ELSE 0 END;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId_Id",
                table: "HealingComponentManifestEntries",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId", "Id" });

            migrationBuilder.CreateTable(
                name: "HealingComponentManifestAssemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicKeyToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RelativePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifestAssemblies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentManifestAssemblies_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId_ComponentEntryId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.ComponentEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "ManifestId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestAssemblies_ManifestId_ComponentEntryId_RelativePath",
                table: "HealingComponentManifestAssemblies",
                columns: new[] { "ManifestId", "ComponentEntryId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestAssemblies_WorkspaceId_ApplicationId_ManifestId_ComponentEntryId",
                table: "HealingComponentManifestAssemblies",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId", "ComponentEntryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingComponentManifestAssemblies");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId_Id",
                table: "HealingComponentManifestEntries");

            migrationBuilder.Sql(
                """
                UPDATE [HealingComponentManifestEntries]
                SET [Kind] = CASE [Kind] WHEN 1 THEN 0 WHEN 2 THEN 1 WHEN 3 THEN 2 ELSE 0 END;
                """);

            migrationBuilder.DropColumn(
                name: "KindName",
                table: "HealingComponentManifestEntries");

            migrationBuilder.AlterColumn<string>(
                name: "RelativePath",
                table: "HealingComponentManifestEntries",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId",
                table: "HealingComponentManifestEntries",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId" });
        }
    }
}
