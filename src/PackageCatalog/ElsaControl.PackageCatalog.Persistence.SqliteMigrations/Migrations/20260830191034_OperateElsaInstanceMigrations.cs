using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class OperateElsaInstanceMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastRequestHash",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "SourceReleaseAttemptCount",
                table: "ElsaInstanceMigrations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReleaseClaimToken",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceReleaseClaimedUntil",
                table: "ElsaInstanceMigrations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseDiagnosticCode",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartRequestHash",
                table: "ElsaInstanceMigrations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "MigrationId",
                table: "ElsaInstanceAuditEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                WITH RankedActive AS (
                    SELECT MigrationId,
                           row_number() OVER (PARTITION BY InstanceId ORDER BY UpdatedAt DESC, MigrationId DESC) AS Position
                    FROM ElsaInstanceMigrations
                    WHERE Phase <> 'RolledBack' AND Phase <> 'Released' AND Phase <> 'Failed'
                )
                UPDATE ElsaInstanceMigrations
                SET Phase = 'Failed'
                WHERE MigrationId IN (SELECT MigrationId FROM RankedActive WHERE Position > 1);

                UPDATE ElsaInstanceMigrations
                SET OperationId = MigrationId,
                    StartRequestHash = lower(replace(MigrationId, '-', '') || replace(MigrationId, '-', '')),
                    LastRequestHash = lower(replace(MigrationId, '-', '') || replace(MigrationId, '-', ''));

                INSERT INTO ElsaInstanceOperations
                    (Id, InstanceId, OrganizationId, WorkspaceId, Action, IdempotencyScope, IdempotencyKey,
                     RequestHash, ExpectedVersion, State, AttemptNumber, AcceptedAt, StartedAt, CompletedAt,
                     CreatedAt, UpdatedAt)
                SELECT m.MigrationId, m.InstanceId, m.OrganizationId, m.WorkspaceId, 'MajorMigration',
                       'legacy-migration/' || m.MigrationId, 'legacy-migration-' || m.MigrationId,
                       m.StartRequestHash, i.Version,
                       CASE
                           WHEN m.Phase IN ('Cutover', 'RetainingSource', 'RetiringSource') THEN 'Running'
                           WHEN m.Phase IN ('Planned', 'Preparing', 'ProvisioningTarget', 'Validating') THEN 'RecoveryRequired'
                           WHEN m.Phase = 'Failed' THEN 'Failed'
                           ELSE 'Succeeded'
                       END,
                       1, m.CreatedAt, m.CreatedAt, m.UpdatedAt, m.CreatedAt, m.UpdatedAt
                FROM ElsaInstanceMigrations m
                JOIN ElsaInstances i ON i.Id = m.InstanceId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId",
                table: "ElsaInstanceMigrations",
                column: "InstanceId",
                unique: true,
                filter: "Phase <> 'RolledBack' AND Phase <> 'Released' AND Phase <> 'Failed'");

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId_StartRequestHash",
                table: "ElsaInstanceMigrations",
                columns: new[] { "InstanceId", "StartRequestHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_OperationId",
                table: "ElsaInstanceMigrations",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_Phase_SourceRetainUntil_SourceReleaseClaimedUntil",
                table: "ElsaInstanceMigrations",
                columns: new[] { "Phase", "SourceRetainUntil", "SourceReleaseClaimedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceAuditEvents_MigrationId",
                table: "ElsaInstanceAuditEvents",
                column: "MigrationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ElsaInstanceMigrations_ElsaInstanceOperations_OperationId",
                table: "ElsaInstanceMigrations",
                column: "OperationId",
                principalTable: "ElsaInstanceOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ElsaInstanceMigrations_NoDelete;
                CREATE TRIGGER TR_ElsaInstanceMigrations_NoDelete
                BEFORE DELETE ON ElsaInstanceMigrations
                BEGIN SELECT RAISE(ABORT, 'Elsa instance migrations are append-only.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ElsaInstanceMigrations_ElsaInstanceOperations_OperationId",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId_StartRequestHash",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceMigrations_OperationId",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceMigrations_Phase_SourceRetainUntil_SourceReleaseClaimedUntil",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceAuditEvents_MigrationId",
                table: "ElsaInstanceAuditEvents");

            migrationBuilder.DropColumn(
                name: "LastRequestHash",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseAttemptCount",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseClaimToken",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseClaimedUntil",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "SourceReleaseDiagnosticCode",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "StartRequestHash",
                table: "ElsaInstanceMigrations");

            migrationBuilder.DropColumn(
                name: "MigrationId",
                table: "ElsaInstanceAuditEvents");
        }
    }
}
