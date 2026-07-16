using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AllowProviderReauthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealingProviderConnections_WorkspaceId_Provider_RepositoryProviderId",
                table: "HealingProviderConnections");

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderConnections_WorkspaceId_Provider_RepositoryProviderId",
                table: "HealingProviderConnections",
                columns: new[] { "WorkspaceId", "Provider", "RepositoryProviderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealingProviderConnections_WorkspaceId_Provider_RepositoryProviderId",
                table: "HealingProviderConnections");

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderConnections_WorkspaceId_Provider_RepositoryProviderId",
                table: "HealingProviderConnections",
                columns: new[] { "WorkspaceId", "Provider", "RepositoryProviderId" },
                unique: true);
        }
    }
}
