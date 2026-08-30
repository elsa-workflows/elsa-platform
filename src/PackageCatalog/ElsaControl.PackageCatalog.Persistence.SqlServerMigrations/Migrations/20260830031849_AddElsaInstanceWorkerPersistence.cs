using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ElsaInstanceResolvedPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: false),
                    PlanUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SerializedPlan = table.Column<string>(type: "nvarchar(max)", maxLength: 1048576, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
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
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceResolvedPlans_AppendOnly
                ON ElsaInstanceResolvedPlans
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51007, ''Elsa instance resolved plans are append-only'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceResolvedPlans_AppendOnly;");

            migrationBuilder.DropTable(
                name: "ElsaInstanceResolvedPlans");

            migrationBuilder.DropColumn(
                name: "LeaseVersion",
                table: "ElsaInstanceOperations");
        }
    }
}
