using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceDatabaseGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceAuditEvents_AppendOnly_Update
                BEFORE UPDATE ON ElsaInstanceAuditEvents
                BEGIN SELECT RAISE(ABORT, 'Elsa instance audit events are append-only'); END;
                CREATE TRIGGER TR_ElsaInstanceAuditEvents_AppendOnly_Delete
                BEFORE DELETE ON ElsaInstanceAuditEvents
                BEGIN SELECT RAISE(ABORT, 'Elsa instance audit events are append-only'); END;
                CREATE TRIGGER TR_ElsaInstanceMigrations_NoDelete
                BEFORE DELETE ON ElsaInstanceMigrations
                BEGIN SELECT RAISE(ABORT, 'Elsa instance migrations are durable'); END;
                CREATE TRIGGER TR_ElsaInstances_NoDelete
                BEFORE DELETE ON ElsaInstances
                BEGIN SELECT RAISE(ABORT, 'Elsa instances are tombstones'); END;
                CREATE TRIGGER TR_ElsaInstanceOperations_NoDelete
                BEFORE DELETE ON ElsaInstanceOperations
                BEGIN SELECT RAISE(ABORT, 'Elsa instance operations are durable'); END;
                CREATE TRIGGER TR_DeploymentRuns_ManagedInstanceBinding_Insert
                BEFORE INSERT ON DeploymentRuns
                WHEN EXISTS (
                    SELECT 1 FROM DeploymentEnvironments e
                    WHERE e.Id = NEW.EnvironmentId
                      AND e.ElsaInstanceId IS NOT NULL
                      AND (NEW.ElsaInstanceId IS NULL
                           OR e.WorkspaceId <> NEW.WorkspaceId
                           OR e.ElsaInstanceId <> NEW.ElsaInstanceId))
                     OR (NEW.ElsaInstanceId IS NOT NULL AND NOT EXISTS (
                         SELECT 1 FROM DeploymentEnvironments e
                         WHERE e.Id = NEW.EnvironmentId
                           AND e.WorkspaceId = NEW.WorkspaceId
                           AND e.ElsaInstanceId = NEW.ElsaInstanceId))
                BEGIN SELECT RAISE(ABORT, 'Managed deployment run binding mismatch'); END;
                CREATE TRIGGER TR_DeploymentRuns_ManagedInstanceBinding_Update
                BEFORE UPDATE OF WorkspaceId, EnvironmentId, ElsaInstanceId ON DeploymentRuns
                WHEN (NEW.ElsaInstanceId IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM DeploymentEnvironments e
                    WHERE e.Id = NEW.EnvironmentId
                      AND e.WorkspaceId = NEW.WorkspaceId
                      AND e.ElsaInstanceId = NEW.ElsaInstanceId))
                     OR (NEW.ElsaInstanceId IS NULL AND EXISTS (
                         SELECT 1 FROM DeploymentEnvironments e
                         WHERE e.Id = NEW.EnvironmentId
                           AND e.ElsaInstanceId IS NOT NULL))
                     OR (OLD.ElsaInstanceId IS NOT NULL AND
                         (NEW.ElsaInstanceId IS NULL OR NEW.ElsaInstanceId <> OLD.ElsaInstanceId))
                BEGIN SELECT RAISE(ABORT, 'Managed deployment run binding mismatch'); END;
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_DeploymentEnvironments_ManagedInstanceBinding_Update;
                DROP TRIGGER IF EXISTS TR_DeploymentRuns_ManagedInstanceBinding_Update;
                DROP TRIGGER IF EXISTS TR_DeploymentRuns_ManagedInstanceBinding_Insert;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_NoDelete;
                DROP TRIGGER IF EXISTS TR_ElsaInstances_NoDelete;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceAuditEvents_AppendOnly_Delete;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceAuditEvents_AppendOnly_Update;
                """);
        }
    }
}
