using System;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260830043000_HardenElsaInstanceWorkerQueue")]
    /// <inheritdoc />
    public partial class HardenElsaInstanceWorkerQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QuarantinedAt",
                table: "ElsaInstanceLifecycleOutbox",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineCode",
                table: "ElsaInstanceLifecycleOutbox",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceLifecycleOutbox_AppendOnly_Update;");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceLifecycleOutbox_AppendOnly_Update
                BEFORE UPDATE ON ElsaInstanceLifecycleOutbox
                WHEN OLD.Id <> NEW.Id
                  OR OLD.OrganizationId <> NEW.OrganizationId
                  OR OLD.WorkspaceId <> NEW.WorkspaceId
                  OR OLD.InstanceId <> NEW.InstanceId
                  OR OLD.OperationId <> NEW.OperationId
                  OR OLD.Action <> NEW.Action
                  OR OLD.RequestHash <> NEW.RequestHash
                  OR OLD.CreatedAt <> NEW.CreatedAt
                  OR OLD.QuarantinedAt IS NOT NULL
                  OR OLD.QuarantineCode IS NOT NULL
                  OR NEW.QuarantinedAt IS NULL
                  OR NEW.QuarantineCode <> 'outbox.invalid'
                BEGIN SELECT RAISE(ABORT, 'Elsa instance lifecycle outbox records are append-only'); END;
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

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceOperations_LeaseVersion_Range_Insert
                BEFORE INSERT ON ElsaInstanceOperations
                WHEN NEW.LeaseVersion < 0 OR NEW.LeaseVersion >= 2147483647
                BEGIN SELECT RAISE(ABORT, 'Elsa instance operation lease version is out of range'); END;
                CREATE TRIGGER TR_ElsaInstanceOperations_LeaseVersion_Range_Update
                BEFORE UPDATE OF LeaseVersion ON ElsaInstanceOperations
                WHEN NEW.LeaseVersion < 0 OR NEW.LeaseVersion >= 2147483647
                BEGIN SELECT RAISE(ABORT, 'Elsa instance operation lease version is out of range'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceLifecycleOutbox_AppendOnly_Update;");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_LeaseVersion_Range_Update;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_LeaseVersion_Range_Insert;
                """);

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
                name: "QuarantinedAt",
                table: "ElsaInstanceLifecycleOutbox");

            migrationBuilder.DropColumn(
                name: "QuarantineCode",
                table: "ElsaInstanceLifecycleOutbox");

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceLifecycleOutbox_AppendOnly_Update
                BEFORE UPDATE ON ElsaInstanceLifecycleOutbox
                BEGIN SELECT RAISE(ABORT, 'Elsa instance lifecycle outbox records are append-only'); END;
                """);
        }
    }
}
