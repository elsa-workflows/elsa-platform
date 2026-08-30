using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceDatabaseGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceAuditEvents_AppendOnly
                ON ElsaInstanceAuditEvents
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51000, ''Elsa instance audit events are append-only'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceMigrations_NoDelete
                ON ElsaInstanceMigrations
                INSTEAD OF DELETE
                AS BEGIN
                    THROW 51001, ''Elsa instance migrations are durable'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstances_NoDelete
                ON ElsaInstances
                INSTEAD OF DELETE
                AS BEGIN
                    THROW 51002, ''Elsa instances are tombstones'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceOperations_NoDelete
                ON ElsaInstanceOperations
                INSTEAD OF DELETE
                AS BEGIN
                    THROW 51005, ''Elsa instance operations are durable'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_DeploymentRuns_ManagedInstanceBinding
                ON DeploymentRuns
                AFTER INSERT, UPDATE
                AS BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        LEFT JOIN DeploymentEnvironments e
                          ON e.Id = i.EnvironmentId
                         AND e.WorkspaceId = i.WorkspaceId
                         AND e.ElsaInstanceId = i.ElsaInstanceId
                        WHERE i.ElsaInstanceId IS NOT NULL AND e.Id IS NULL)
                        THROW 51003, ''Managed deployment run binding mismatch'', 1;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN DeploymentEnvironments e ON e.Id = i.EnvironmentId
                        WHERE e.ElsaInstanceId IS NOT NULL
                          AND (i.ElsaInstanceId IS NULL
                               OR e.WorkspaceId <> i.WorkspaceId
                               OR e.ElsaInstanceId <> i.ElsaInstanceId))
                        THROW 51003, ''Managed deployment run binding mismatch'', 1;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.Id = i.Id
                        WHERE d.ElsaInstanceId IS NOT NULL
                          AND (i.ElsaInstanceId IS NULL OR i.ElsaInstanceId <> d.ElsaInstanceId))
                        THROW 51003, ''Managed deployment run binding mismatch'', 1;
                END;');
                """);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DeploymentRuns_ManagedInstanceBinding;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstances_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceAuditEvents_AppendOnly;");
        }
    }
}
