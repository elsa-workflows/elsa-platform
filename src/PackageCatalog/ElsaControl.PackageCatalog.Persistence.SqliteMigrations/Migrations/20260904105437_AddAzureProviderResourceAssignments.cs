using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AzureProviderResourceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderScopeFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NamingVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ResourceGroupName = table.Column<string>(type: "TEXT", maxLength: 90, nullable: false),
                    WorkloadName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OwnershipKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    LastOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FoundationDeploymentId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    WorkloadDeploymentId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    WorkloadResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    WorkloadRevisionName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StableTrafficRevisionName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WorkloadIdentityResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    WorkloadIdentityClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WorkloadIdentityPrincipalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    KeyVaultResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    KeyVaultUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SqlServerResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SqlServerFqdn = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContainerAppsEnvironmentResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RegistryResourceId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    AcrPullDeploymentId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AcrPullRoleAssignmentId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
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
