using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedElsaHandoffPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedElsaHandoffAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Jti = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BindingVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedElsaHandoffAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedElsaHandoffReplayConsumptions",
                columns: table => new
                {
                    Jti = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ConsumedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedElsaHandoffReplayConsumptions", x => x.Jti);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedElsaHandoffAuditEvents_Jti_OccurredAt",
                table: "ManagedElsaHandoffAuditEvents",
                columns: new[] { "Jti", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedElsaHandoffAuditEvents_OccurredAt",
                table: "ManagedElsaHandoffAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedElsaHandoffReplayConsumptions_ExpiresAt",
                table: "ManagedElsaHandoffReplayConsumptions",
                column: "ExpiresAt");

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ManagedElsaHandoffReplayConsumptions_AppendOnly_Update
                BEFORE UPDATE ON ManagedElsaHandoffReplayConsumptions
                BEGIN SELECT RAISE(ABORT, 'Managed Elsa handoff replay records are append-only'); END;
                CREATE TRIGGER TR_ManagedElsaHandoffAuditEvents_AppendOnly_Update
                BEFORE UPDATE ON ManagedElsaHandoffAuditEvents
                BEGIN SELECT RAISE(ABORT, 'Managed Elsa handoff audit events are append-only'); END;
                CREATE TRIGGER TR_ManagedElsaHandoffAuditEvents_AppendOnly_Delete
                BEFORE DELETE ON ManagedElsaHandoffAuditEvents
                BEGIN SELECT RAISE(ABORT, 'Managed Elsa handoff audit events are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffAuditEvents_AppendOnly_Delete;
                DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffAuditEvents_AppendOnly_Update;
                DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffReplayConsumptions_AppendOnly_Update;
                """);

            migrationBuilder.DropTable(
                name: "ManagedElsaHandoffAuditEvents");

            migrationBuilder.DropTable(
                name: "ManagedElsaHandoffReplayConsumptions");
        }
    }
}
