using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingLifecycleNoticesAndCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EarlyDeletionRequestedAt",
                table: "OrganizationSubscriptions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GraceEndsAt",
                table: "OrganizationSubscriptions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleVersion",
                table: "OrganizationSubscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RetentionEndsAt",
                table: "OrganizationSubscriptions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationBillingCleanups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CleanupKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderCustomerReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProviderSubscriptionReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    NotBeforeAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastFailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationBillingCleanups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationBillingCleanups_OrganizationSubscriptions_OrganizationId_SubscriptionId",
                        columns: x => new { x.OrganizationId, x.SubscriptionId },
                        principalTable: "OrganizationSubscriptions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationBillingCleanups_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationBillingLifecycleNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeliveredAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeliveryAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationBillingLifecycleNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationBillingLifecycleNotices_OrganizationSubscriptions_OrganizationId_SubscriptionId",
                        columns: x => new { x.OrganizationId, x.SubscriptionId },
                        principalTable: "OrganizationSubscriptions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationBillingLifecycleNotices_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingCleanups_OrganizationId_SubscriptionId",
                table: "OrganizationBillingCleanups",
                columns: new[] { "OrganizationId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingCleanups_State_NotBeforeAt_LeaseExpiresAt",
                table: "OrganizationBillingCleanups",
                columns: new[] { "State", "NotBeforeAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingCleanups_SubscriptionId",
                table: "OrganizationBillingCleanups",
                column: "SubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationBillingLifecycleNotices_OrganizationId_SubscriptionId_Kind",
                table: "OrganizationBillingLifecycleNotices",
                columns: new[] { "OrganizationId", "SubscriptionId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationBillingCleanups");

            migrationBuilder.DropTable(
                name: "OrganizationBillingLifecycleNotices");

            migrationBuilder.DropColumn(
                name: "EarlyDeletionRequestedAt",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "GraceEndsAt",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "LifecycleVersion",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "RetentionEndsAt",
                table: "OrganizationSubscriptions");
        }
    }
}
