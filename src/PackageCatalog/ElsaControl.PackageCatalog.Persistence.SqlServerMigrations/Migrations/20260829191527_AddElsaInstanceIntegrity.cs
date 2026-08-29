using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TRIGGER TR_ElsaInstanceMigrations_NoDelete ON ElsaInstanceMigrations INSTEAD OF DELETE AS BEGIN THROW 51000, 'Elsa instance migrations are durable', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER TR_ElsaInstances_NoDelete ON ElsaInstances INSTEAD OF DELETE AS BEGIN THROW 51001, 'Elsa instances are tombstones', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER TR_ElsaInstanceOperations_CreateOnly ON ElsaInstanceOperations AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE InstanceId IS NULL AND Action <> 'Create') THROW 51002, 'Only create operations may omit instance', 1; END;");
            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentRuns_DeploymentEnvironments_WorkspaceId_EnvironmentId_ElsaInstanceId",
                table: "DeploymentRuns");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId_ElsaInstanceId",
                table: "DeploymentRuns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DeploymentEnvironments_WorkspaceId_Id_ElsaInstanceId",
                table: "DeploymentEnvironments");

            migrationBuilder.AlterColumn<Guid>(
                name: "ElsaInstanceId",
                table: "DeploymentEnvironments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstances_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_CreateOnly;");
            migrationBuilder.DropColumn("OrganizationId", "ElsaInstanceMigrations");
            migrationBuilder.DropColumn("WorkspaceId", "ElsaInstanceMigrations");
            migrationBuilder.AlterColumn<Guid>(
                name: "ElsaInstanceId",
                table: "DeploymentEnvironments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DeploymentEnvironments_WorkspaceId_Id_ElsaInstanceId",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "Id", "ElsaInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId_ElsaInstanceId",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId", "ElsaInstanceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentRuns_DeploymentEnvironments_WorkspaceId_EnvironmentId_ElsaInstanceId",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId", "ElsaInstanceId" },
                principalTable: "DeploymentEnvironments",
                principalColumns: new[] { "WorkspaceId", "Id", "ElsaInstanceId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
