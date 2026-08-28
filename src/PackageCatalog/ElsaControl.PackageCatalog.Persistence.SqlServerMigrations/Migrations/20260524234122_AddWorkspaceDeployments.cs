using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentApplications_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentEnvironments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DesiredRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeployedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeploymentStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DriftStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentEnvironments_DeploymentApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "DeploymentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DesiredStateRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Commit = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DesiredStateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthoredAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesiredStateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesiredStateRevisions_DeploymentEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "DeploymentEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowEngines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CertificateStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CredentialProvider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CredentialReference = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    CredentialVerificationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CredentialLastVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Health = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HostingProvider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEngines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEngines_DeploymentEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "DeploymentEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EngineCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapabilityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Boundary = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineCapabilities_WorkflowEngines_EngineId",
                        column: x => x.EngineId,
                        principalTable: "WorkflowEngines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeControls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Boundary = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequiredCapabilityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeControls_WorkflowEngines_EngineId",
                        column: x => x.EngineId,
                        principalTable: "WorkflowEngines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentApplications_WorkspaceId_Name",
                table: "DeploymentApplications",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_ApplicationId",
                table: "DeploymentEnvironments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_WorkspaceId_ApplicationId_Name",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "ApplicationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesiredStateRevisions_EnvironmentId_RevisionNumber",
                table: "DesiredStateRevisions",
                columns: new[] { "EnvironmentId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesiredStateRevisions_WorkspaceId_ContentHash",
                table: "DesiredStateRevisions",
                columns: new[] { "WorkspaceId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineCapabilities_EngineId_CapabilityId",
                table: "EngineCapabilities",
                columns: new[] { "EngineId", "CapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControls_EngineId_ControlId",
                table: "RuntimeControls",
                columns: new[] { "EngineId", "ControlId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEngines_EnvironmentId",
                table: "WorkflowEngines",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEngines_WorkspaceId_EnvironmentId_Name",
                table: "WorkflowEngines",
                columns: new[] { "WorkspaceId", "EnvironmentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DesiredStateRevisions");

            migrationBuilder.DropTable(
                name: "EngineCapabilities");

            migrationBuilder.DropTable(
                name: "RuntimeControls");

            migrationBuilder.DropTable(
                name: "WorkflowEngines");

            migrationBuilder.DropTable(
                name: "DeploymentEnvironments");

            migrationBuilder.DropTable(
                name: "DeploymentApplications");
        }
    }
}
