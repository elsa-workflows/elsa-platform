using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddWeaverAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeaverSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CopilotSessionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RoutePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReasoningEffort = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeaverMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RedactionState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeaverMessages_WeaverSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WeaverSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeaverPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PlanType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    TargetJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImpactJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RollbackJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Risk = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeaverPlans_WeaverSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WeaverSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeaverToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ArgumentsHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResultSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizationResult = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverToolCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeaverToolCalls_WeaverSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WeaverSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeaverPlanApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersion = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PermissionSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverPlanApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeaverPlanApprovals_WeaverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WeaverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeaverPlanExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LinkedResourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StartedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaverPlanExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeaverPlanExecutions_WeaverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WeaverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeaverMessages_SessionId_Sequence",
                table: "WeaverMessages",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeaverPlanApprovals_PlanId_PlanVersion_AccountId",
                table: "WeaverPlanApprovals",
                columns: new[] { "PlanId", "PlanVersion", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeaverPlanExecutions_PlanId_PlanVersion",
                table: "WeaverPlanExecutions",
                columns: new[] { "PlanId", "PlanVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeaverPlans_SessionId_Version",
                table: "WeaverPlans",
                columns: new[] { "SessionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeaverSessions_WorkspaceId_CreatedAt",
                table: "WeaverSessions",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WeaverToolCalls_SessionId_CreatedAt",
                table: "WeaverToolCalls",
                columns: new[] { "SessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeaverMessages");

            migrationBuilder.DropTable(
                name: "WeaverPlanApprovals");

            migrationBuilder.DropTable(
                name: "WeaverPlanExecutions");

            migrationBuilder.DropTable(
                name: "WeaverToolCalls");

            migrationBuilder.DropTable(
                name: "WeaverPlans");

            migrationBuilder.DropTable(
                name: "WeaverSessions");
        }
    }
}
