using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationBillingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionId",
                table: "OrganizationEntitlementSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionState",
                table: "OrganizationEntitlementSnapshots",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingProviderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderEventId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EventHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    ProviderCustomerReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProviderSubscriptionReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RejectionCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingProviderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingProviderEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderCustomerReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProviderSubscriptionReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TrialStartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TrialEndsAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    PastDueAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ConstrainedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SuspendedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RetainedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastProviderEventOccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastProviderEventId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSubscriptions", x => x.Id);
                    table.UniqueConstraint("AK_OrganizationSubscriptions_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingProviderEvents_OrganizationId_OccurredAt",
                table: "BillingProviderEvents",
                columns: new[] { "OrganizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingProviderEvents_Provider_ProviderEventId",
                table: "BillingProviderEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_OrganizationId",
                table: "OrganizationSubscriptions",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_Provider_ProviderCustomerReference",
                table: "OrganizationSubscriptions",
                columns: new[] { "Provider", "ProviderCustomerReference" },
                unique: true,
                filter: "ProviderCustomerReference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_Provider_ProviderSubscriptionReference",
                table: "OrganizationSubscriptions",
                columns: new[] { "Provider", "ProviderSubscriptionReference" },
                unique: true,
                filter: "ProviderSubscriptionReference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEntitlementSnapshots_OrganizationId_SubscriptionId",
                table: "OrganizationEntitlementSnapshots",
                columns: new[] { "OrganizationId", "SubscriptionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationEntitlementSnapshots_OrganizationSubscriptions_OrganizationId_SubscriptionId",
                table: "OrganizationEntitlementSnapshots",
                columns: new[] { "OrganizationId", "SubscriptionId" },
                principalTable: "OrganizationSubscriptions",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationSubscriptions_Provider_ProviderCustomerReference",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSubscriptions_Provider_ProviderSubscriptionReference",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationEntitlementSnapshots_OrganizationSubscriptions_OrganizationId_SubscriptionId",
                table: "OrganizationEntitlementSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationEntitlementSnapshots_OrganizationId_SubscriptionId",
                table: "OrganizationEntitlementSnapshots");

            migrationBuilder.DropTable(
                name: "BillingProviderEvents");

            migrationBuilder.DropTable(
                name: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "OrganizationEntitlementSnapshots");

            migrationBuilder.DropColumn(
                name: "SubscriptionState",
                table: "OrganizationEntitlementSnapshots");
        }
    }
}
