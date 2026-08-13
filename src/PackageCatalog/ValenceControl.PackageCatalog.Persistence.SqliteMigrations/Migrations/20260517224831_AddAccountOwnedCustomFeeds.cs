using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountOwnedCustomFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerWorkspaceId",
                table: "PackageSources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "PackageSources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SoftDeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentities_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceEntitlementSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanCreateCustomSources = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxSources = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPackagesIndexed = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxVersionsPerPackage = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxSyncsPerDay = table.Column<int>(type: "INTEGER", nullable: true),
                    PrivateFeedsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEntitlementSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceEntitlementSnapshots_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageSources_OwnerWorkspaceId",
                table: "PackageSources",
                column: "OwnerWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_AccountId",
                table: "ExternalIdentities",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Issuer_Subject",
                table: "ExternalIdentities",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEntitlementSnapshots_WorkspaceId",
                table: "WorkspaceEntitlementSnapshots",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_AccountId",
                table: "WorkspaceMemberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_WorkspaceId_AccountId",
                table: "WorkspaceMemberships",
                columns: new[] { "WorkspaceId", "AccountId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageSources_Workspaces_OwnerWorkspaceId",
                table: "PackageSources",
                column: "OwnerWorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageSources_Workspaces_OwnerWorkspaceId",
                table: "PackageSources");

            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "WorkspaceEntitlementSnapshots");

            migrationBuilder.DropTable(
                name: "WorkspaceMemberships");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_PackageSources_OwnerWorkspaceId",
                table: "PackageSources");

            migrationBuilder.DropColumn(
                name: "OwnerWorkspaceId",
                table: "PackageSources");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "PackageSources");
        }
    }
}
