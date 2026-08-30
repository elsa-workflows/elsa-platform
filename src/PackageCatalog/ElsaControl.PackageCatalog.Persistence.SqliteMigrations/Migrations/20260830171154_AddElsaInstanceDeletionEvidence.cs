using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceDeletionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletionDiagnosticCode",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceDigest",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceFingerprint",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceReference",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding_Update;
                CREATE TRIGGER TR_DeploymentEnvironments_ManagedInstanceBinding_Update
                BEFORE UPDATE OF WorkspaceId, Id, ElsaInstanceId ON DeploymentEnvironments
                WHEN EXISTS (
                    SELECT 1 FROM DeploymentRuns r
                    WHERE r.EnvironmentId = OLD.Id
                      AND r.WorkspaceId = OLD.WorkspaceId
                      AND r.ElsaInstanceId IS NOT NULL
                      AND (NEW.ElsaInstanceId IS NULL
                           OR r.WorkspaceId <> NEW.WorkspaceId
                           OR r.ElsaInstanceId <> NEW.ElsaInstanceId))
                  AND NOT (
                    NEW.ElsaInstanceId IS NULL
                    AND OLD.ElsaInstanceId IS NOT NULL
                    AND NEW.WorkspaceId = OLD.WorkspaceId
                    AND EXISTS (
                        SELECT 1 FROM ElsaInstances i
                        WHERE i.Id = OLD.ElsaInstanceId
                          AND i.WorkspaceId = OLD.WorkspaceId
                          AND i.ObservedLifecycle = 'Deleted')
                    AND NOT EXISTS (
                        SELECT 1 FROM DeploymentRuns active
                        WHERE active.EnvironmentId = OLD.Id
                          AND active.WorkspaceId = OLD.WorkspaceId
                          AND active.ElsaInstanceId = OLD.ElsaInstanceId
                          AND active.Status IN ('Queued', 'Running', 'RecoveryRequired')))
                BEGIN SELECT RAISE(ABORT, 'Managed environment binding is referenced by a deployment run'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding_Update;
                CREATE TRIGGER TR_DeploymentEnvironments_ManagedInstanceBinding_Update
                BEFORE UPDATE OF WorkspaceId, Id, ElsaInstanceId ON DeploymentEnvironments
                WHEN EXISTS (
                    SELECT 1 FROM DeploymentRuns r
                    WHERE r.EnvironmentId = OLD.Id
                      AND r.WorkspaceId = OLD.WorkspaceId
                      AND r.ElsaInstanceId IS NOT NULL
                      AND (NEW.ElsaInstanceId IS NULL
                           OR r.WorkspaceId <> NEW.WorkspaceId
                           OR r.ElsaInstanceId <> NEW.ElsaInstanceId))
                BEGIN SELECT RAISE(ABORT, 'Managed environment binding is referenced by a deployment run'); END;
                """);

            migrationBuilder.DropColumn(
                name: "DeletionDiagnosticCode",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "DeletionEvidenceDigest",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "DeletionEvidenceFingerprint",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "DeletionEvidenceReference",
                table: "ElsaInstanceOperations");
        }
    }
}
