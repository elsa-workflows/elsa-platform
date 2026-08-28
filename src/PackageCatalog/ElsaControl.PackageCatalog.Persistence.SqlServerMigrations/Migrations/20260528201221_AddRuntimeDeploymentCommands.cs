using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeDeploymentCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClaimedAt = table.Column<long>(type: "bigint", nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    HeartbeatAt = table.Column<long>(type: "bigint", nullable: true),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    PercentComplete = table.Column<int>(type: "int", nullable: true),
                    ProgressMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ObservedArtifactDigestAlgorithm = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ObservedArtifactDigest = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RuntimeReference = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    AvailableAt = table.Column<long>(type: "bigint", nullable: true),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentCommands_DeploymentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "DeploymentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentCommandWebhookNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SafePayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    SentAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentCommandWebhookNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentCommandEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentCommandEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentCommandEvents_DeploymentCommands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "DeploymentCommands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommandEvents_CommandId",
                table: "DeploymentCommandEvents",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommandEvents_WorkspaceId_CommandId_CreatedAt",
                table: "DeploymentCommandEvents",
                columns: new[] { "WorkspaceId", "CommandId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommandEvents_WorkspaceId_RunId_CreatedAt",
                table: "DeploymentCommandEvents",
                columns: new[] { "WorkspaceId", "RunId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommands_RunId",
                table: "DeploymentCommands",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommands_WorkspaceId_EngineId_Status_AvailableAt",
                table: "DeploymentCommands",
                columns: new[] { "WorkspaceId", "EngineId", "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommands_WorkspaceId_IdempotencyKey",
                table: "DeploymentCommands",
                columns: new[] { "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommandWebhookNotifications_CommandId",
                table: "DeploymentCommandWebhookNotifications",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCommandWebhookNotifications_WorkspaceId_EngineId_CreatedAt",
                table: "DeploymentCommandWebhookNotifications",
                columns: new[] { "WorkspaceId", "EngineId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentCommandEvents");

            migrationBuilder.DropTable(
                name: "DeploymentCommandWebhookNotifications");

            migrationBuilder.DropTable(
                name: "DeploymentCommands");
        }
    }
}
