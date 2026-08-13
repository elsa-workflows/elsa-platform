using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.Healing.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedInferenceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealingManagedRepairInferenceReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceContextDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReservedInferenceUnits = table.Column<long>(type: "bigint", nullable: false),
                    LeaseTokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OutcomeCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingManagedRepairInferenceReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingManagedRepairInferenceReservations_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingManagedRepairInferenceReservations_LeaseTokenHash",
                table: "HealingManagedRepairInferenceReservations",
                column: "LeaseTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingManagedRepairInferenceReservations_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingManagedRepairInferenceReservations",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingManagedRepairInferenceReservations");
        }
    }
}
