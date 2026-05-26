using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeControlExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuntimeControlExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ControlLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Boundary = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequiredCapabilityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeControlExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeControlExecutions_WorkflowEngines_EngineId",
                        column: x => x.EngineId,
                        principalTable: "WorkflowEngines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlExecutions_EngineId",
                table: "RuntimeControlExecutions",
                column: "EngineId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlExecutions_WorkspaceId_EngineId_ControlId_CreatedAt",
                table: "RuntimeControlExecutions",
                columns: new[] { "WorkspaceId", "EngineId", "ControlId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeControlExecutions");
        }
    }
}
