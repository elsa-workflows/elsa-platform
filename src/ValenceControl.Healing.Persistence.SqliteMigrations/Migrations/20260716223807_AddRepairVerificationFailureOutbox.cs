using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.Healing.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairVerificationFailureOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealingRepairVerificationFailureOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncidentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupportingOccurrenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NextAttemptAt = table.Column<long>(type: "INTEGER", nullable: true),
                    OutcomeCode = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeliveredAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingRepairVerificationFailureOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingRepairVerificationFailureOutbox_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairVerificationFailureOutbox_HealingIncidentOccurrences_WorkspaceId_ApplicationId_SupportingOccurrenceId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.SupportingOccurrenceId },
                        principalTable: "HealingIncidentOccurrences",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairVerificationFailureOutbox_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId },
                        principalTable: "HealingIncidents",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairVerificationFailureOutbox_Status_NextAttemptAt_LeaseExpiresAt",
                table: "HealingRepairVerificationFailureOutbox",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairVerificationFailureOutbox_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingRepairVerificationFailureOutbox",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairVerificationFailureOutbox_WorkspaceId_ApplicationId_IdempotencyKey",
                table: "HealingRepairVerificationFailureOutbox",
                columns: new[] { "WorkspaceId", "ApplicationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairVerificationFailureOutbox_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingRepairVerificationFailureOutbox",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairVerificationFailureOutbox_WorkspaceId_ApplicationId_SupportingOccurrenceId",
                table: "HealingRepairVerificationFailureOutbox",
                columns: new[] { "WorkspaceId", "ApplicationId", "SupportingOccurrenceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingRepairVerificationFailureOutbox");
        }
    }
}
