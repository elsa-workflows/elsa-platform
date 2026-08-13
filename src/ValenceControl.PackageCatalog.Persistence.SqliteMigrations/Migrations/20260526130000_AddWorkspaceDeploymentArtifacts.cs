using System;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260526130000_AddWorkspaceDeploymentArtifacts")]
    public partial class AddWorkspaceDeploymentArtifacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceDeploymentArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LayoutVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ContentDigestAlgorithm = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContentDigest = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReferenceProvider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ManifestName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ManifestVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ManifestEnvironment = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResourceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceSummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChecksumStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InspectionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RegisteredByAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastInspectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceDeploymentArtifacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceDeploymentArtifacts_WorkspaceId_ArtifactId",
                table: "WorkspaceDeploymentArtifacts",
                columns: new[] { "WorkspaceId", "ArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceDeploymentArtifacts_WorkspaceId_RegisteredAt",
                table: "WorkspaceDeploymentArtifacts",
                columns: new[] { "WorkspaceId", "RegisteredAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkspaceDeploymentArtifacts");
        }
    }
}
