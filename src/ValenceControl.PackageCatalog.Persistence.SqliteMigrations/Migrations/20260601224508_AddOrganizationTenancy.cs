using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "Workspaces",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchivedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CustomerReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorSubject = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAuditRecords_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationEntitlementSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanCreateCustomSources = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxSources = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxWorkspaces = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPackagesIndexed = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxVersionsPerPackage = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxSyncsPerDay = table.Column<int>(type: "INTEGER", nullable: true),
                    PrivateFeedsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManagedHostingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeploymentTargetsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEntitlementSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationEntitlementSnapshots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DisabledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    InvitedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO Organizations (Id, Name, Status, CreatedAt, UpdatedAt, ArchivedAt, CreatedByAccountId, CustomerReference)
                SELECT Id, Name, 'Active', CreatedAt, UpdatedAt, NULL, NULL, NULL
                FROM Workspaces
                WHERE OrganizationId IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE Workspaces
                SET OrganizationId = Id
                WHERE OrganizationId IS NULL;
                """);

            migrationBuilder.Sql("""
                INSERT INTO OrganizationMemberships (Id, OrganizationId, AccountId, Role, CreatedAt, UpdatedAt, DisabledAt, InvitedByAccountId)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))),
                    w.OrganizationId,
                    wm.AccountId,
                    CASE WHEN wm.Role = 2 THEN 'Owner' ELSE 'Member' END,
                    wm.CreatedAt,
                    wm.UpdatedAt,
                    NULL,
                    NULL
                FROM WorkspaceMemberships wm
                INNER JOIN Workspaces w ON w.Id = wm.WorkspaceId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM OrganizationMemberships existing
                    WHERE existing.OrganizationId = w.OrganizationId
                      AND existing.AccountId = wm.AccountId
                );
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                table: "Workspaces",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OrganizationId_SoftDeletedAt_Name",
                table: "Workspaces",
                columns: new[] { "OrganizationId", "SoftDeletedAt", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditRecords_OrganizationId_CreatedAt",
                table: "OrganizationAuditRecords",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEntitlementSnapshots_OrganizationId",
                table: "OrganizationEntitlementSnapshots",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_AccountId",
                table: "OrganizationMemberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId_AccountId",
                table: "OrganizationMemberships",
                columns: new[] { "OrganizationId", "AccountId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Workspaces_Organizations_OrganizationId",
                table: "Workspaces",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workspaces_Organizations_OrganizationId",
                table: "Workspaces");

            migrationBuilder.DropTable(
                name: "OrganizationAuditRecords");

            migrationBuilder.DropTable(
                name: "OrganizationEntitlementSnapshots");

            migrationBuilder.DropTable(
                name: "OrganizationMemberships");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OrganizationId_SoftDeletedAt_Name",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Workspaces");
        }
    }
}
