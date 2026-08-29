using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TRIGGER IF NOT EXISTS TR_ElsaInstanceMigrations_NoDelete BEFORE DELETE ON ElsaInstanceMigrations BEGIN SELECT RAISE(ABORT, 'Elsa instance migrations are durable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER IF NOT EXISTS TR_ElsaInstances_NoDelete BEFORE DELETE ON ElsaInstances BEGIN SELECT RAISE(ABORT, 'Elsa instances are tombstones'); END;");
            migrationBuilder.Sql("CREATE TRIGGER IF NOT EXISTS TR_ElsaInstanceOperations_CreateOnly BEFORE INSERT ON ElsaInstanceOperations WHEN NEW.InstanceId IS NULL AND NEW.Action <> 'Create' BEGIN SELECT RAISE(ABORT, 'Only create operations may omit instance'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstances_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceOperations_CreateOnly;");
            migrationBuilder.DropColumn("OrganizationId", "ElsaInstanceMigrations");
            migrationBuilder.DropColumn("WorkspaceId", "ElsaInstanceMigrations");
        }
    }
}
