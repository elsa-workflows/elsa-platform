using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactArchival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ArchivedAt",
                table: "WorkspaceDeploymentArtifacts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByAccountId",
                table: "WorkspaceDeploymentArtifacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WorkspaceDeploymentArtifacts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceDeploymentArtifacts_WorkspaceId_Status_RegisteredAt",
                table: "WorkspaceDeploymentArtifacts",
                columns: new[] { "WorkspaceId", "Status", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkspaceDeploymentArtifacts_WorkspaceId_Status_RegisteredAt",
                table: "WorkspaceDeploymentArtifacts");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "WorkspaceDeploymentArtifacts");

            migrationBuilder.DropColumn(
                name: "ArchivedByAccountId",
                table: "WorkspaceDeploymentArtifacts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WorkspaceDeploymentArtifacts");
        }
    }
}
