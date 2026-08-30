using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260830110432_AddAzureProviderOperationGlobalQueueIndex")]
    public partial class AddAzureProviderOperationGlobalQueueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_Status_LeaseExpiresAt_UpdatedAt",
                table: "AzureProviderOperations");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_Status_LeaseExpiresAt_UpdatedAt_Id",
                table: "AzureProviderOperations",
                columns: new[] { "Status", "LeaseExpiresAt", "UpdatedAt", "Id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_Status_LeaseExpiresAt_UpdatedAt_Id",
                table: "AzureProviderOperations");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_Status_LeaseExpiresAt_UpdatedAt",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "Status", "LeaseExpiresAt", "UpdatedAt" });
        }
    }
}
