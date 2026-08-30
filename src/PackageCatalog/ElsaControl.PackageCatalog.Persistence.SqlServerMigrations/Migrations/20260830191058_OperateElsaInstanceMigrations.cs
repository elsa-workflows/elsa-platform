using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "ElsaInstanceMigrations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "SourceReleaseAttemptCount",
                table: "ElsaInstanceMigrations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReleaseClaimToken",
                table: "ElsaInstanceMigrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceReleaseClaimedUntil",
                table: "ElsaInstanceMigrations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReleaseDiagnosticCode",
                table: "ElsaInstanceMigrations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartRequestHash",
                table: "ElsaInstanceMigrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "MigrationId",
                table: "ElsaInstanceAuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ElsaInstanceMigrations
                SET Phase = CASE WHEN Phase IN ('Released', 'RolledBack', 'Failed') THEN Phase ELSE 'Failed' END,
                    OperationId = MigrationId,
                    StartRequestHash = lower(replace(convert(varchar(36), MigrationId), '-', '') + replace(convert(varchar(36), MigrationId), '-', '')),
                    LastRequestHash = lower(replace(convert(varchar(36), MigrationId), '-', '') + replace(convert(varchar(36), MigrationId), '-', ''));

                INSERT INTO ElsaInstanceOperations
                    (Id, InstanceId, OrganizationId, WorkspaceId, Action, IdempotencyScope, IdempotencyKey,
                     RequestHash, ExpectedVersion, State, AttemptNumber, AcceptedAt, StartedAt, CompletedAt,
                     CreatedAt, UpdatedAt)
                SELECT m.MigrationId, m.InstanceId, m.OrganizationId, m.WorkspaceId, 'MajorMigration',
                       'legacy-migration/' + convert(varchar(36), m.MigrationId),
                       'legacy-migration-' + convert(varchar(36), m.MigrationId),
                       m.StartRequestHash, i.Version,
                       CASE WHEN m.Phase = 'Failed' THEN 'Failed' ELSE 'Succeeded' END,
                       1, m.CreatedAt, m.CreatedAt, m.UpdatedAt, m.CreatedAt, m.UpdatedAt
                FROM ElsaInstanceMigrations m
                JOIN ElsaInstances i ON i.Id = m.InstanceId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId",
                table: "ElsaInstanceMigrations",
                column: "InstanceId",
                unique: true,
                filter: "Phase NOT IN ('RolledBack', 'Released', 'Failed')");

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
