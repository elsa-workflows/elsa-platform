using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspacePermissionAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GrantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
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
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    WorkspaceId, Id, AccountId, Permission, 'Granted', GrantedByAccountId, CreatedAt
                FROM WorkspacePermissionGrants;

                INSERT INTO WorkspacePermissionAuditRecords
                    (Id, WorkspaceId, GrantId, AccountId, Permission, Action, ActorAccountId, OccurredAt)
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    WorkspaceId, Id, AccountId, Permission, 'Revoked', NULL, RevokedAt
                FROM WorkspacePermissionGrants
                WHERE RevokedAt IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                INSERT INTO WorkspacePermissionGrants
                    (Id, WorkspaceId, AccountId, Permission, GrantedByAccountId, CreatedAt, UpdatedAt, RevokedAt, RevokedByAccountId)
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    membership.WorkspaceId,
                    membership.AccountId,
                    permission.Name,
                    membership.AccountId,
                    CAST((julianday('now') - julianday('0001-01-01')) * 864000000000 AS INTEGER),
                    CAST((julianday('now') - julianday('0001-01-01')) * 864000000000 AS INTEGER),
                    NULL,
                    NULL
                FROM WorkspaceMemberships AS membership
                CROSS JOIN (
                    SELECT 'deployments.read' AS Name
                    UNION ALL SELECT 'deployments.setup.manage'
                    UNION ALL SELECT 'deployments.desired-state.manage'
                    UNION ALL SELECT 'deployments.promotion.preview'
                    UNION ALL SELECT 'deployments.run.execute'
                    UNION ALL SELECT 'deployments.rollback.execute'
                    UNION ALL SELECT 'deployments.controls.execute'
                    UNION ALL SELECT 'deployments.observability.manage'
                    UNION ALL SELECT 'healing.read'
                    UNION ALL SELECT 'healing.configure'
                    UNION ALL SELECT 'healing.evidence.elevate'
                    UNION ALL SELECT 'healing.repair.retry'
                    UNION ALL SELECT 'healing.repair.stop'
                    UNION ALL SELECT 'healing.verification.waive'
                    UNION ALL SELECT 'healing.automerge.configure'
                ) AS permission
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
                SELECT
                    upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    grant.WorkspaceId, grant.Id, grant.AccountId, grant.Permission, 'Granted', grant.GrantedByAccountId, grant.CreatedAt
                FROM WorkspacePermissionGrants AS grant
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM WorkspacePermissionAuditRecords AS audit
                    WHERE audit.GrantId = grant.Id AND audit.Action = 'Granted'
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
        }
    }
}
