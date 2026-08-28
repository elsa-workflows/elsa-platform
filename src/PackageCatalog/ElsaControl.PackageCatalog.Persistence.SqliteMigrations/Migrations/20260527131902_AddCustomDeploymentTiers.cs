using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomDeploymentTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TierId",
                table: "DeploymentEnvironments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TierRequiresReview",
                table: "DeploymentEnvironments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeploymentTierDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ArchivedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentTierDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentTierDefinitions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentTierCapabilityAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapabilityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentTierCapabilityAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentTierCapabilityAssignments_DeploymentTierDefinitions_TierId",
                        column: x => x.TierId,
                        principalTable: "DeploymentTierDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentTierChangeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ChangedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AffectedEnvironmentCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentTierChangeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentTierChangeRecords_DeploymentTierDefinitions_TierId",
                        column: x => x.TierId,
                        principalTable: "DeploymentTierDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_TierId",
                table: "DeploymentEnvironments",
                column: "TierId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_WorkspaceId_TierId",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "TierId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierCapabilityAssignments_TierId_CapabilityId",
                table: "DeploymentTierCapabilityAssignments",
                columns: new[] { "TierId", "CapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierCapabilityAssignments_WorkspaceId_CapabilityId",
                table: "DeploymentTierCapabilityAssignments",
                columns: new[] { "WorkspaceId", "CapabilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierChangeRecords_TierId",
                table: "DeploymentTierChangeRecords",
                column: "TierId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierChangeRecords_WorkspaceId_TierId_ChangedAt",
                table: "DeploymentTierChangeRecords",
                columns: new[] { "WorkspaceId", "TierId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierDefinitions_WorkspaceId_SortOrder",
                table: "DeploymentTierDefinitions",
                columns: new[] { "WorkspaceId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTierDefinitions_WorkspaceId_Status_Name",
                table: "DeploymentTierDefinitions",
                columns: new[] { "WorkspaceId", "Status", "Name" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO DeploymentTierDefinitions (
                    Id,
                    WorkspaceId,
                    Name,
                    Description,
                    SortOrder,
                    IsDefault,
                    Status,
                    CreatedAt,
                    UpdatedAt,
                    CreatedByAccountId,
                    UpdatedByAccountId,
                    ArchivedAt,
                    ArchivedByAccountId
                )
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    w.Id,
                    defaults.Name,
                    NULL,
                    defaults.SortOrder,
                    1,
                    'Active',
                    0,
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL
                FROM Workspaces w
                CROSS JOIN (
                    SELECT 'Dev' AS Name, 10 AS SortOrder
                    UNION ALL SELECT 'Test', 20
                    UNION ALL SELECT 'Stage', 30
                    UNION ALL SELECT 'Production', 40
                ) defaults
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM DeploymentTierDefinitions existing
                    WHERE existing.WorkspaceId = w.Id
                      AND existing.Status = 'Active'
                      AND existing.Name = defaults.Name
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO DeploymentTierCapabilityAssignments (
                    Id,
                    WorkspaceId,
                    TierId,
                    CapabilityId,
                    CreatedAt,
                    CreatedByAccountId
                )
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    tiers.WorkspaceId,
                    tiers.Id,
                    capabilities.CapabilityId,
                    0,
                    NULL
                FROM DeploymentTierDefinitions tiers
                JOIN (
                    SELECT 'Dev' AS TierName, 'deployment.tier.development-like' AS CapabilityId
                    UNION ALL SELECT 'Dev', 'deployment.promotion.source'
                    UNION ALL SELECT 'Test', 'deployment.tier.test-like'
                    UNION ALL SELECT 'Test', 'deployment.promotion.source'
                    UNION ALL SELECT 'Test', 'deployment.promotion.target'
                    UNION ALL SELECT 'Stage', 'deployment.tier.preproduction-like'
                    UNION ALL SELECT 'Stage', 'deployment.promotion.source'
                    UNION ALL SELECT 'Stage', 'deployment.promotion.target'
                    UNION ALL SELECT 'Stage', 'deployment.secret-verification.required'
                    UNION ALL SELECT 'Production', 'deployment.tier.production-like'
                    UNION ALL SELECT 'Production', 'deployment.promotion.target'
                    UNION ALL SELECT 'Production', 'deployment.confirmation.required'
                    UNION ALL SELECT 'Production', 'deployment.rollback.enabled'
                    UNION ALL SELECT 'Production', 'deployment.secret-verification.required'
                    UNION ALL SELECT 'Production', 'deployment.observability.required'
                ) capabilities ON capabilities.TierName = tiers.Name
                WHERE tiers.IsDefault = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM DeploymentTierCapabilityAssignments existing
                      WHERE existing.TierId = tiers.Id
                        AND existing.CapabilityId = capabilities.CapabilityId
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE DeploymentEnvironments
                SET TierId = (
                    SELECT tiers.Id
                    FROM DeploymentTierDefinitions tiers
                    WHERE tiers.WorkspaceId = DeploymentEnvironments.WorkspaceId
                      AND tiers.Status = 'Active'
                      AND tiers.Name = DeploymentEnvironments.Tier
                    ORDER BY tiers.IsDefault DESC, tiers.SortOrder
                    LIMIT 1
                )
                WHERE TierId IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE DeploymentEnvironments
                SET TierRequiresReview = 1
                WHERE TierId IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentEnvironments_DeploymentTierDefinitions_TierId",
                table: "DeploymentEnvironments",
                column: "TierId",
                principalTable: "DeploymentTierDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentEnvironments_DeploymentTierDefinitions_TierId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropTable(
                name: "DeploymentTierCapabilityAssignments");

            migrationBuilder.DropTable(
                name: "DeploymentTierChangeRecords");

            migrationBuilder.DropTable(
                name: "DeploymentTierDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentEnvironments_TierId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentEnvironments_WorkspaceId_TierId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropColumn(
                name: "TierId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropColumn(
                name: "TierRequiresReview",
                table: "DeploymentEnvironments");
        }
    }
}
