using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceWorkerPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaseVersion",
                table: "ElsaInstanceOperations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ElsaInstanceResolvedPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    PlanUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SerializedPlan = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceResolvedPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceResolvedPlans_ElsaInstances_OrganizationId_WorkspaceId_InstanceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId, x.InstanceId },
                        principalTable: "ElsaInstances",
                        principalColumns: new[] { "OrganizationId", "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceResolvedPlans_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceResolvedPlans_Workspaces_OrganizationId_WorkspaceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId },
                        principalTable: "Workspaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceResolvedPlans_OrganizationId_WorkspaceId_InstanceId",
                table: "ElsaInstanceResolvedPlans",
                columns: new[] { "OrganizationId", "WorkspaceId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceResolvedPlans_WorkspaceId_ContentHash",
                table: "ElsaInstanceResolvedPlans",
                columns: new[] { "WorkspaceId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceResolvedPlans_WorkspaceId_InstanceId_PlanId",
                table: "ElsaInstanceResolvedPlans",
                columns: new[] { "WorkspaceId", "InstanceId", "PlanId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceResolvedPlans_AppendOnly_Update
                BEFORE UPDATE ON ElsaInstanceResolvedPlans
                BEGIN SELECT RAISE(ABORT, 'Elsa instance resolved plans are append-only'); END;
                CREATE TRIGGER TR_ElsaInstanceResolvedPlans_AppendOnly_Delete
                BEFORE DELETE ON ElsaInstanceResolvedPlans
                BEGIN SELECT RAISE(ABORT, 'Elsa instance resolved plans are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ElsaInstanceResolvedPlans_AppendOnly_Delete;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceResolvedPlans_AppendOnly_Update;
                """);

            migrationBuilder.DropTable(
                name: "ElsaInstanceResolvedPlans");

            migrationBuilder.DropColumn(
                name: "LeaseVersion",
                table: "ElsaInstanceOperations");
        }
    }
}
