using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentFoundationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConfirmedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    UsedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionConfirmations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousDeployedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RollbackSourceRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ValidationOutcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueuedAt = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkerHeartbeatAt = table.Column<long>(type: "bigint", nullable: true),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    RecoveryReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentRuns_DeploymentEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "DeploymentEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriftReportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Desired = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Observed = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DetectedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftReportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriftReportItems_DeploymentEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "DeploymentEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObservabilityBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    CorrelatedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sample = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservabilityBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObservabilityBindings_DeploymentEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "DeploymentEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StructuredDesiredStateRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredDesiredStateRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StructuredDesiredStateRecords_DesiredStateRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DesiredStateRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspacePermissionGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GrantedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePermissionGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspacePermissionGrants_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspacePermissionGrants_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentRunHistoryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentRunHistoryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentRunHistoryEvents_DeploymentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "DeploymentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionConfirmations_WorkspaceId_ActionType_TargetId_ConfirmedByAccountId",
                table: "ActionConfirmations",
                columns: new[] { "WorkspaceId", "ActionType", "TargetId", "ConfirmedByAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRunHistoryEvents_RunId",
                table: "DeploymentRunHistoryEvents",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRunHistoryEvents_WorkspaceId_RunId_CreatedAt",
                table: "DeploymentRunHistoryEvents",
                columns: new[] { "WorkspaceId", "RunId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_EnvironmentId",
                table: "DeploymentRuns",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId_Status",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DriftReportItems_EnvironmentId",
                table: "DriftReportItems",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DriftReportItems_WorkspaceId_EnvironmentId_EngineId_DetectedAt",
                table: "DriftReportItems",
                columns: new[] { "WorkspaceId", "EnvironmentId", "EngineId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityBindings_EnvironmentId",
                table: "ObservabilityBindings",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityBindings_WorkspaceId_EnvironmentId_Kind",
                table: "ObservabilityBindings",
                columns: new[] { "WorkspaceId", "EnvironmentId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_StructuredDesiredStateRecords_RevisionId_Kind_Name",
                table: "StructuredDesiredStateRecords",
                columns: new[] { "RevisionId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructuredDesiredStateRecords_WorkspaceId_ContentHash",
                table: "StructuredDesiredStateRecords",
                columns: new[] { "WorkspaceId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionGrants_AccountId",
                table: "WorkspacePermissionGrants",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionGrants_WorkspaceId_AccountId_Permission_RevokedAt",
                table: "WorkspacePermissionGrants",
                columns: new[] { "WorkspaceId", "AccountId", "Permission", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionConfirmations");

            migrationBuilder.DropTable(
                name: "DeploymentRunHistoryEvents");

            migrationBuilder.DropTable(
                name: "DriftReportItems");

            migrationBuilder.DropTable(
                name: "ObservabilityBindings");

            migrationBuilder.DropTable(
                name: "StructuredDesiredStateRecords");

            migrationBuilder.DropTable(
                name: "WorkspacePermissionGrants");

            migrationBuilder.DropTable(
                name: "DeploymentRuns");
        }
    }
}
