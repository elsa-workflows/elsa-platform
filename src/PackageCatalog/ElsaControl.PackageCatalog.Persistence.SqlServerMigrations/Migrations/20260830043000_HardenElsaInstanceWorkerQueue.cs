using System;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260830043000_HardenElsaInstanceWorkerQueue")]
    /// <inheritdoc />
    public partial class HardenElsaInstanceWorkerQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ElsaInstanceOperations_LeaseVersion_Range",
                table: "ElsaInstanceOperations",
                sql: "LeaseVersion >= 0 AND LeaseVersion < 2147483647");

            migrationBuilder.AddColumn<long>(
                name: "QuarantinedAt",
                table: "ElsaInstanceLifecycleOutbox",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineCode",
                table: "ElsaInstanceLifecycleOutbox",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceLifecycleOutbox_AppendOnly;");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceLifecycleOutbox_AppendOnly
                ON ElsaInstanceLifecycleOutbox
                AFTER UPDATE, DELETE
                AS BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        FULL OUTER JOIN deleted d ON d.Id = i.Id
                        WHERE i.Id IS NULL OR d.Id IS NULL
                           OR i.OrganizationId <> d.OrganizationId
                           OR i.WorkspaceId <> d.WorkspaceId
                           OR i.InstanceId <> d.InstanceId
                           OR i.OperationId <> d.OperationId
                           OR i.Action <> d.Action
                           OR i.RequestHash <> d.RequestHash
                           OR i.CreatedAt <> d.CreatedAt
                           OR d.QuarantinedAt IS NOT NULL
                           OR d.QuarantineCode IS NOT NULL
                           OR i.QuarantinedAt IS NULL
                           OR i.QuarantineCode <> ''outbox.invalid''
                    )
                        THROW 51006, ''Elsa instance lifecycle outbox records are append-only'', 1;
                END;');
                """);

            migrationBuilder.DropIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId" },
                unique: true,
                filter: "Status IN ('Queued', 'Running', 'RecoveryRequired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceLifecycleOutbox_AppendOnly;");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId" },
                unique: true,
                filter: "ElsaInstanceId IS NOT NULL AND Status IN ('Queued', 'Running', 'RecoveryRequired')");

            migrationBuilder.DropColumn(
                name: "QuarantineCode",
                table: "ElsaInstanceLifecycleOutbox");

            migrationBuilder.DropColumn(
                name: "QuarantinedAt",
                table: "ElsaInstanceLifecycleOutbox");

            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceLifecycleOutbox_AppendOnly
                ON ElsaInstanceLifecycleOutbox
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51006, ''Elsa instance lifecycle outbox records are append-only'', 1;
                END;');
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ElsaInstanceOperations_LeaseVersion_Range",
                table: "ElsaInstanceOperations");
        }
    }
}
