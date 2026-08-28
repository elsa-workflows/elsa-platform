using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspacePermissionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByAccountId",
                table: "WorkspacePermissionGrants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProtectedSecret",
                table: "DeploymentCredentialReferences",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspacePermissionAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePermissionAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspacePermissionAuditRecords_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspacePermissionAuditRecords_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionAuditRecords_AccountId",
                table: "WorkspacePermissionAuditRecords",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionAuditRecords_GrantId_Action",
                table: "WorkspacePermissionAuditRecords",
                columns: new[] { "GrantId", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionAuditRecords_WorkspaceId_AccountId_OccurredAt",
                table: "WorkspacePermissionAuditRecords",
                columns: new[] { "WorkspaceId", "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePermissionAuditRecords_WorkspaceId_OccurredAt_Id",
                table: "WorkspacePermissionAuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt", "Id" });

            migrationBuilder.Sql("""
                INSERT INTO WorkspacePermissionAuditRecords
                    (Id, WorkspaceId, GrantId, AccountId, Permission, Action, ActorAccountId, OccurredAt)
                SELECT NEWID(), WorkspaceId, Id, AccountId, Permission, 'Granted', GrantedByAccountId, CreatedAt
                FROM WorkspacePermissionGrants;

                INSERT INTO WorkspacePermissionAuditRecords
                    (Id, WorkspaceId, GrantId, AccountId, Permission, Action, ActorAccountId, OccurredAt)
                SELECT NEWID(), WorkspaceId, Id, AccountId, Permission, 'Revoked', NULL, RevokedAt
                FROM WorkspacePermissionGrants
                WHERE RevokedAt IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                DECLARE @Now datetime2 = SYSUTCDATETIME();
                DECLARE @NowTicks bigint =
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, '0001-01-01'), @Now) * 864000000000
                    + DATEDIFF_BIG(NANOSECOND, CONVERT(date, @Now), @Now) / 100;

                INSERT INTO WorkspacePermissionGrants
                    (Id, WorkspaceId, AccountId, Permission, GrantedByAccountId, CreatedAt, UpdatedAt, RevokedAt, RevokedByAccountId)
                SELECT
                    NEWID(),
                    membership.WorkspaceId,
                    membership.AccountId,
                    permission.Name,
                    membership.AccountId,
                    @NowTicks,
                    @NowTicks,
                    NULL,
                    NULL
                FROM WorkspaceMemberships AS membership
                CROSS JOIN (VALUES
                    ('deployments.read'),
                    ('deployments.setup.manage'),
                    ('deployments.desired-state.manage'),
                    ('deployments.promotion.preview'),
                    ('deployments.run.execute'),
                    ('deployments.rollback.execute'),
                    ('deployments.controls.execute'),
                    ('deployments.observability.manage'),
                    ('healing.read'),
                    ('healing.configure'),
                    ('healing.evidence.elevate'),
                    ('healing.repair.retry'),
                    ('healing.repair.stop'),
                    ('healing.verification.waive'),
                    ('healing.automerge.configure')
                ) AS permission(Name)
                WHERE membership.Role = 2
                  AND NOT EXISTS (
                      SELECT 1
                      FROM WorkspacePermissionGrants AS existing
                      WHERE existing.WorkspaceId = membership.WorkspaceId
                        AND existing.AccountId = membership.AccountId
                        AND existing.Permission = permission.Name
                  );

                INSERT INTO WorkspacePermissionAuditRecords
                    (Id, WorkspaceId, GrantId, AccountId, Permission, Action, ActorAccountId, OccurredAt)
                SELECT NEWID(), grantRow.WorkspaceId, grantRow.Id, grantRow.AccountId, grantRow.Permission, 'Granted', grantRow.GrantedByAccountId, grantRow.CreatedAt
                FROM WorkspacePermissionGrants AS grantRow
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM WorkspacePermissionAuditRecords AS audit
                    WHERE audit.GrantId = grantRow.Id AND audit.Action = 'Granted'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspacePermissionAuditRecords");

            migrationBuilder.DropColumn(
                name: "RevokedByAccountId",
                table: "WorkspacePermissionGrants");

            migrationBuilder.AlterColumn<string>(
                name: "ProtectedSecret",
                table: "DeploymentCredentialReferences",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 4096,
                oldNullable: true);
        }
    }
}
