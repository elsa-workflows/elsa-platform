using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.Healing.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeVerificationAndReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SafeDecisionReason",
                table: "HealingVerificationResults",
                type: "TEXT",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WaiverExpiresAt",
                table: "HealingVerificationResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "HealingHumanCommands",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderActorLogin",
                table: "HealingHumanCommands",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceObservationId",
                table: "HealingDeploymentObservations",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Existing rows must receive distinct stable values before the unique indexes are created.
            migrationBuilder.Sql("""
                UPDATE "HealingHumanCommands"
                SET "IdempotencyKey" = CAST("Id" AS TEXT)
                WHERE "IdempotencyKey" = '';
                """);
            migrationBuilder.Sql("""
                UPDATE "HealingDeploymentObservations"
                SET "SourceObservationId" = CAST("Id" AS TEXT)
                WHERE "SourceObservationId" = '';
                """);

            migrationBuilder.CreateTable(
                name: "HealingProviderActorIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProviderActorLogin = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ControlAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerifiedByAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingProviderActorIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingProviderActorIdentityLinks_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingHumanCommands_WorkspaceId_IncidentId_IdempotencyKey",
                table: "HealingHumanCommands",
                columns: new[] { "WorkspaceId", "IncidentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingDeploymentObservations_WorkspaceId_ApplicationId_Source_SourceObservationId",
                table: "HealingDeploymentObservations",
                columns: new[] { "WorkspaceId", "ApplicationId", "Source", "SourceObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderActorIdentityLinks_WorkspaceId_ControlAccountId",
                table: "HealingProviderActorIdentityLinks",
                columns: new[] { "WorkspaceId", "ControlAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderActorIdentityLinks_WorkspaceId_ProviderConnectionId_ProviderActorId",
                table: "HealingProviderActorIdentityLinks",
                columns: new[] { "WorkspaceId", "ProviderConnectionId", "ProviderActorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingProviderActorIdentityLinks");

            migrationBuilder.DropIndex(
                name: "IX_HealingHumanCommands_WorkspaceId_IncidentId_IdempotencyKey",
                table: "HealingHumanCommands");

            migrationBuilder.DropIndex(
                name: "IX_HealingDeploymentObservations_WorkspaceId_ApplicationId_Source_SourceObservationId",
                table: "HealingDeploymentObservations");

            migrationBuilder.DropColumn(
                name: "SafeDecisionReason",
                table: "HealingVerificationResults");

            migrationBuilder.DropColumn(
                name: "WaiverExpiresAt",
                table: "HealingVerificationResults");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "HealingHumanCommands");

            migrationBuilder.DropColumn(
                name: "ProviderActorLogin",
                table: "HealingHumanCommands");

            migrationBuilder.DropColumn(
                name: "SourceObservationId",
                table: "HealingDeploymentObservations");
        }
    }
}
