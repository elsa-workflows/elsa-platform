using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Jti = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BindingVersion = table.Column<int>(type: "int", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedElsaHandoffAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedElsaHandoffReplayConsumptions",
                columns: table => new
                {
                    Jti = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: false)
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

            // Dynamic EXEC keeps CREATE TRIGGER statements safe for generated
            // idempotent scripts, whose parser compiles the whole batch before
            // migration history guards are evaluated.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ManagedElsaHandoffReplayConsumptions_AppendOnly
                ON ManagedElsaHandoffReplayConsumptions
                INSTEAD OF UPDATE
                AS BEGIN
                    THROW 51010, ''Managed Elsa handoff replay records are append-only'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ManagedElsaHandoffAuditEvents_AppendOnly
                ON ManagedElsaHandoffAuditEvents
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51011, ''Managed Elsa handoff audit events are append-only'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffAuditEvents_AppendOnly;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ManagedElsaHandoffReplayConsumptions_AppendOnly;");

            migrationBuilder.DropTable(
                name: "ManagedElsaHandoffAuditEvents");

            migrationBuilder.DropTable(
                name: "ManagedElsaHandoffReplayConsumptions");
        }
    }
}
