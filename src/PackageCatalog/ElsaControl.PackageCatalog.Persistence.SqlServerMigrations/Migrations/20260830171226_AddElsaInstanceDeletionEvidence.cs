using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceDigest",
                table: "ElsaInstanceOperations",
                type: "nvarchar(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceFingerprint",
                table: "ElsaInstanceOperations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionEvidenceReference",
                table: "ElsaInstanceOperations",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding;");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_DeploymentEnvironments_ManagedInstanceBinding
                ON DeploymentEnvironments
                AFTER UPDATE
                AS BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.Id = i.Id
                        JOIN DeploymentRuns r
                          ON r.EnvironmentId = d.Id
                         AND r.WorkspaceId = d.WorkspaceId
                         AND r.ElsaInstanceId IS NOT NULL
                        WHERE (i.ElsaInstanceId IS NULL
                               OR r.WorkspaceId <> i.WorkspaceId
                               OR r.ElsaInstanceId <> i.ElsaInstanceId)
                          AND NOT (
                              i.ElsaInstanceId IS NULL
                              AND d.ElsaInstanceId IS NOT NULL
                              AND i.WorkspaceId = d.WorkspaceId
                              AND EXISTS (
                                  SELECT 1 FROM ElsaInstances instance
                                  WHERE instance.Id = d.ElsaInstanceId
                                    AND instance.WorkspaceId = d.WorkspaceId
                                    AND instance.ObservedLifecycle = ''Deleted'')
                              AND NOT EXISTS (
                                  SELECT 1 FROM DeploymentRuns active
                                  WHERE active.EnvironmentId = d.Id
                                    AND active.WorkspaceId = d.WorkspaceId
                                    AND active.ElsaInstanceId = d.ElsaInstanceId
                                    AND active.Status IN (''Queued'', ''Running'', ''RecoveryRequired''))))
                        THROW 51004, ''Managed environment binding is referenced by a deployment run'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding;");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_DeploymentEnvironments_ManagedInstanceBinding
                ON DeploymentEnvironments
                AFTER UPDATE
                AS BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.Id = i.Id
                        JOIN DeploymentRuns r
                          ON r.EnvironmentId = d.Id
                         AND r.WorkspaceId = d.WorkspaceId
                         AND r.ElsaInstanceId IS NOT NULL
                        WHERE i.ElsaInstanceId IS NULL
                           OR r.WorkspaceId <> i.WorkspaceId
                           OR r.ElsaInstanceId <> i.ElsaInstanceId)
                        THROW 51004, ''Managed environment binding is referenced by a deployment run'', 1;
                END;');
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
