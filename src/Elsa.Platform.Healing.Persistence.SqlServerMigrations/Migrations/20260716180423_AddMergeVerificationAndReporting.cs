using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqlServerMigrations.Migrations
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
                type: "nvarchar(max)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WaiverExpiresAt",
                table: "HealingVerificationResults",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "HealingHumanCommands",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderActorLogin",
                table: "HealingHumanCommands",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceObservationId",
                table: "HealingDeploymentObservations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Existing rows must receive distinct stable values before the unique indexes are created.
            migrationBuilder.Sql("""
                UPDATE [HealingHumanCommands]
                SET [IdempotencyKey] = CONVERT(nvarchar(36), [Id])
                WHERE [IdempotencyKey] = N'';
                """);
            migrationBuilder.Sql("""
                UPDATE [HealingDeploymentObservations]
                SET [SourceObservationId] = CONVERT(nvarchar(36), [Id])
                WHERE [SourceObservationId] = N'';
                """);

            migrationBuilder.CreateTable(
                name: "HealingProviderActorIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderActorLogin = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlatformAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifiedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifiedAt = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
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
                name: "IX_HealingProviderActorIdentityLinks_WorkspaceId_PlatformAccountId",
                table: "HealingProviderActorIdentityLinks",
                columns: new[] { "WorkspaceId", "PlatformAccountId" });

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
