using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TierRequiresReview",
                table: "DeploymentEnvironments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeploymentTierDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ArchivedAt = table.Column<long>(type: "bigint", nullable: true),
                    ArchivedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapabilityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ChangedAt = table.Column<long>(type: "bigint", nullable: false),
                    AffectedEnvironmentCount = table.Column<int>(type: "int", nullable: false)
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
                    NEWID(),
                    w.Id,
                    defaults.Name,
                    NULL,
                    defaults.SortOrder,
                    CAST(1 AS bit),
                    'Active',
                    0,
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL
                FROM Workspaces w
                CROSS JOIN (
                    VALUES
                        ('Dev', 10),
                        ('Test', 20),
                        ('Stage', 30),
                        ('Production', 40)
                ) defaults(Name, SortOrder)
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
                    NEWID(),
                    tiers.WorkspaceId,
                    tiers.Id,
                    capabilities.CapabilityId,
                    0,
                    NULL
                FROM DeploymentTierDefinitions tiers
                JOIN (
                    VALUES
                        ('Dev', 'deployment.tier.development-like'),
                        ('Dev', 'deployment.promotion.source'),
                        ('Test', 'deployment.tier.test-like'),
                        ('Test', 'deployment.promotion.source'),
                        ('Test', 'deployment.promotion.target'),
                        ('Stage', 'deployment.tier.preproduction-like'),
                        ('Stage', 'deployment.promotion.source'),
                        ('Stage', 'deployment.promotion.target'),
                        ('Stage', 'deployment.secret-verification.required'),
                        ('Production', 'deployment.tier.production-like'),
                        ('Production', 'deployment.promotion.target'),
                        ('Production', 'deployment.confirmation.required'),
                        ('Production', 'deployment.rollback.enabled'),
                        ('Production', 'deployment.secret-verification.required'),
                        ('Production', 'deployment.observability.required')
                ) capabilities(TierName, CapabilityId) ON capabilities.TierName = tiers.Name
                WHERE tiers.IsDefault = CAST(1 AS bit)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM DeploymentTierCapabilityAssignments existing
                      WHERE existing.TierId = tiers.Id
                        AND existing.CapabilityId = capabilities.CapabilityId
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE environments
                SET TierId = tiers.Id
                FROM DeploymentEnvironments environments
                JOIN DeploymentTierDefinitions tiers
                  ON tiers.WorkspaceId = environments.WorkspaceId
                 AND tiers.Status = 'Active'
                 AND tiers.Name = environments.Tier
                WHERE environments.TierId IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE DeploymentEnvironments
                SET TierRequiresReview = CAST(1 AS bit)
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
