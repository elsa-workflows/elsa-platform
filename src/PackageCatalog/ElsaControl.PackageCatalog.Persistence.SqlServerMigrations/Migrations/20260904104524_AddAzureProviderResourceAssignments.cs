using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderResourceAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderAssignmentId",
                table: "AzureProviderOperations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AzureProviderResourceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderScopeFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NamingVersion = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ResourceGroupName = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    WorkloadName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OwnershipKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FoundationDeploymentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    WorkloadDeploymentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    WorkloadResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    WorkloadRevisionName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StableTrafficRevisionName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WorkloadIdentityResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    WorkloadIdentityClientId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WorkloadIdentityPrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    KeyVaultResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    KeyVaultUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    SqlServerResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SqlServerFqdn = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ContainerAppsEnvironmentResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    RegistryResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AcrPullDeploymentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    AcrPullRoleAssignmentId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureProviderResourceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AzureProviderResourceAssignments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_ProviderAssignmentId",
                table: "AzureProviderOperations",
                column: "ProviderAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderResourceAssignments_State_UpdatedAt_Id",
                table: "AzureProviderResourceAssignments",
                columns: new[] { "State", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderResourceAssignments_WorkspaceId_InstanceId_ProviderScopeFingerprint",
                table: "AzureProviderResourceAssignments",
                columns: new[] { "WorkspaceId", "InstanceId", "ProviderScopeFingerprint" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AzureProviderOperations_AzureProviderResourceAssignments_ProviderAssignmentId",
                table: "AzureProviderOperations",
                column: "ProviderAssignmentId",
                principalTable: "AzureProviderResourceAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AzureProviderOperations_AzureProviderResourceAssignments_ProviderAssignmentId",
                table: "AzureProviderOperations");

            migrationBuilder.DropTable(
                name: "AzureProviderResourceAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_ProviderAssignmentId",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "ProviderAssignmentId",
                table: "AzureProviderOperations");
        }
    }
}
