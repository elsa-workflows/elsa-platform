using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderTargetSerialization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'Running', 'RecoveryRequired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations");
        }
    }
}
