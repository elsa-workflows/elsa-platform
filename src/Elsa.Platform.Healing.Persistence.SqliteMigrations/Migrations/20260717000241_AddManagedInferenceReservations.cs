using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqliteMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SourceContextDigest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReservedInferenceUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseTokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OutcomeCode = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
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
