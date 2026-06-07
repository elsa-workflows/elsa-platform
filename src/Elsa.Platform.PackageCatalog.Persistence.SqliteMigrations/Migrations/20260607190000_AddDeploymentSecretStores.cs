using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentSecretStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CredentialReferenceId",
                table: "WorkflowEngines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeploymentSecretStores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_DeploymentSecretStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentSecretStores_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentCredentialReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretStoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastVerifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ArchivedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentCredentialReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentCredentialReferences_DeploymentSecretStores_SecretStoreId",
                        column: x => x.SecretStoreId,
                        principalTable: "DeploymentSecretStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEngines_WorkspaceId_CredentialReferenceId",
                table: "WorkflowEngines",
                columns: new[] { "WorkspaceId", "CredentialReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEngines_CredentialReferenceId",
                table: "WorkflowEngines",
                column: "CredentialReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentSecretStores_WorkspaceId_Status_Name",
                table: "DeploymentSecretStores",
                columns: new[] { "WorkspaceId", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCredentialReferences_SecretStoreId_Status_Name",
                table: "DeploymentCredentialReferences",
                columns: new[] { "SecretStoreId", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentCredentialReferences_WorkspaceId_Status",
                table: "DeploymentCredentialReferences",
                columns: new[] { "WorkspaceId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowEngines_DeploymentCredentialReferences_CredentialReferenceId",
                table: "WorkflowEngines",
                column: "CredentialReferenceId",
                principalTable: "DeploymentCredentialReferences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowEngines_DeploymentCredentialReferences_CredentialReferenceId",
                table: "WorkflowEngines");

            migrationBuilder.DropTable(name: "DeploymentCredentialReferences");
            migrationBuilder.DropTable(name: "DeploymentSecretStores");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowEngines_WorkspaceId_CredentialReferenceId",
                table: "WorkflowEngines");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowEngines_CredentialReferenceId",
                table: "WorkflowEngines");

            migrationBuilder.DropColumn(
                name: "CredentialReferenceId",
                table: "WorkflowEngines");
        }
    }
}
