using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ArchivedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperatorSubject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanCreateCustomSources = table.Column<bool>(type: "bit", nullable: false),
                    MaxSources = table.Column<int>(type: "int", nullable: false),
                    MaxWorkspaces = table.Column<int>(type: "int", nullable: false),
                    MaxPackagesIndexed = table.Column<int>(type: "int", nullable: true),
                    MaxVersionsPerPackage = table.Column<int>(type: "int", nullable: true),
                    MaxSyncsPerDay = table.Column<int>(type: "int", nullable: true),
                    PrivateFeedsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ManagedHostingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DeploymentTargetsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SyncedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    DisabledAt = table.Column<long>(type: "bigint", nullable: true),
                    InvitedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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

            // Workspaces store timestamps as datetimeoffset, while the organization tables store
            // DateTimeOffset.UtcTicks as bigint (see OrganizationConfiguration). Copying the columns
            // straight across is an operand type clash, so convert each value to UTC ticks.
            migrationBuilder.Sql("""
                EXEC(N'INSERT INTO Organizations (Id, Name, Status, CreatedAt, UpdatedAt, ArchivedAt, CreatedByAccountId, CustomerReference)
                SELECT
                    w.Id,
                    w.Name,
                    ''Active'',
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, ''0001-01-01''), utc.CreatedAt) * 864000000000
                        + DATEDIFF_BIG(NANOSECOND, CONVERT(date, utc.CreatedAt), utc.CreatedAt) / 100,
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, ''0001-01-01''), utc.UpdatedAt) * 864000000000
                        + DATEDIFF_BIG(NANOSECOND, CONVERT(date, utc.UpdatedAt), utc.UpdatedAt) / 100,
                    NULL,
                    NULL,
                    NULL
                FROM Workspaces AS w
                CROSS APPLY (VALUES (
                    CONVERT(datetime2, SWITCHOFFSET(w.CreatedAt, 0)),
                    CONVERT(datetime2, SWITCHOFFSET(w.UpdatedAt, 0))
                )) AS utc(CreatedAt, UpdatedAt)
                WHERE w.OrganizationId IS NULL;');
                """);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE Workspaces
                SET OrganizationId = Id
                WHERE OrganizationId IS NULL;');
                """);

            migrationBuilder.Sql("""
                EXEC(N'INSERT INTO OrganizationMemberships (Id, OrganizationId, AccountId, Role, CreatedAt, UpdatedAt, DisabledAt, InvitedByAccountId)
                SELECT
                    NEWID(),
                    w.OrganizationId,
                    wm.AccountId,
                    CASE WHEN wm.Role = 2 THEN ''Owner'' ELSE ''Member'' END,
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, ''0001-01-01''), utc.CreatedAt) * 864000000000
                        + DATEDIFF_BIG(NANOSECOND, CONVERT(date, utc.CreatedAt), utc.CreatedAt) / 100,
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, ''0001-01-01''), utc.UpdatedAt) * 864000000000
                        + DATEDIFF_BIG(NANOSECOND, CONVERT(date, utc.UpdatedAt), utc.UpdatedAt) / 100,
                    NULL,
                    NULL
                FROM WorkspaceMemberships wm
                INNER JOIN Workspaces w ON w.Id = wm.WorkspaceId
                CROSS APPLY (VALUES (
                    CONVERT(datetime2, SWITCHOFFSET(wm.CreatedAt, 0)),
                    CONVERT(datetime2, SWITCHOFFSET(wm.UpdatedAt, 0))
                )) AS utc(CreatedAt, UpdatedAt)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM OrganizationMemberships existing
                    WHERE existing.OrganizationId = w.OrganizationId
                      AND existing.AccountId = wm.AccountId
                );');
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                table: "Workspaces",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
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
