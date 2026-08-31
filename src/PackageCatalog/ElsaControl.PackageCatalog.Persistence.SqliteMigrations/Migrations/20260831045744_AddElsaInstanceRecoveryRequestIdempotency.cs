using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceRecoveryRequestIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryIdempotencyScope",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryRequestHash",
                table: "ElsaInstanceOperations",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_RecoveryIdempotencyScope_RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations",
                columns: new[] { "WorkspaceId", "RecoveryIdempotencyScope", "RecoveryIdempotencyKey" },
                unique: true,
                filter: "RecoveryIdempotencyKey IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_RecoveryIdempotencyScope_RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "RecoveryIdempotencyScope",
                table: "ElsaInstanceOperations");

            migrationBuilder.DropColumn(
                name: "RecoveryRequestHash",
                table: "ElsaInstanceOperations");
        }
    }
}
