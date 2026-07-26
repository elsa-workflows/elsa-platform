using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.Healing.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ProviderConnectionId_IdempotencyKey",
                table: "HealingProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_Digest",
                table: "HealingEvidenceBundles");

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "HealingWorkloadIdentityExchanges",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalId",
                table: "HealingWorkloadIdentityExchanges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopesJson",
                table: "HealingWorkloadIdentityExchanges",
                type: "nvarchar(max)",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowReference",
                table: "HealingSourceOwnershipBindings",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EnvelopeDigest",
                table: "HealingRepairResults",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProposalDigest",
                table: "HealingRepairResults",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalId",
                table: "HealingRepairResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "HealingProviderOperations",
                type: "nvarchar(max)",
                maxLength: 262144,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecretReference",
                table: "HealingProviderConnections",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HealingManagedRepairProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceContextDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProposalDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", maxLength: 1048576, nullable: false),
                    FinalizationNonceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProtectedFinalizationNonce = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingManagedRepairProposals", x => x.Id);
                    table.UniqueConstraint("AK_HealingManagedRepairProposals_WorkspaceId_ApplicationId_AttemptId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingManagedRepairProposals_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingProviderMutationJournal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SafePayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingProviderMutationJournal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingProviderMutationJournal_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingWorkloadHeartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingWorkloadHeartbeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingWorkloadHeartbeats_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairResults_ProposalId",
                table: "HealingRepairResults",
                column: "ProposalId",
                unique: true,
                filter: "[ProposalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_NonceHash",
                table: "HealingRepairAttempts",
                column: "NonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ProviderConnectionId_Kind_IdempotencyKey",
                table: "HealingProviderOperations",
                columns: new[] { "WorkspaceId", "ProviderConnectionId", "Kind", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_Digest",
                table: "HealingEvidenceBundles",
                columns: new[] { "WorkspaceId", "Digest" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingManagedRepairProposals_FinalizationNonceHash",
                table: "HealingManagedRepairProposals",
                column: "FinalizationNonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingManagedRepairProposals_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingManagedRepairProposals",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderMutationJournal_WorkspaceId_ProviderConnectionId_Kind_IdempotencyKey",
                table: "HealingProviderMutationJournal",
                columns: new[] { "WorkspaceId", "ProviderConnectionId", "Kind", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkloadHeartbeats_WorkspaceId_ApplicationId_AttemptId_IdempotencyKey",
                table: "HealingWorkloadHeartbeats",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealingManagedRepairProposals");

            migrationBuilder.DropTable(
                name: "HealingProviderMutationJournal");

            migrationBuilder.DropTable(
                name: "HealingWorkloadHeartbeats");

            migrationBuilder.DropIndex(
                name: "IX_HealingRepairResults_ProposalId",
                table: "HealingRepairResults");

            migrationBuilder.DropIndex(
                name: "IX_HealingRepairAttempts_NonceHash",
                table: "HealingRepairAttempts");

            migrationBuilder.DropIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ProviderConnectionId_Kind_IdempotencyKey",
                table: "HealingProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_Digest",
                table: "HealingEvidenceBundles");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "HealingWorkloadIdentityExchanges");

            migrationBuilder.DropColumn(
                name: "ProposalId",
                table: "HealingWorkloadIdentityExchanges");

            migrationBuilder.DropColumn(
                name: "ScopesJson",
                table: "HealingWorkloadIdentityExchanges");

            migrationBuilder.DropColumn(
                name: "WorkflowReference",
                table: "HealingSourceOwnershipBindings");

            migrationBuilder.DropColumn(
                name: "EnvelopeDigest",
                table: "HealingRepairResults");

            migrationBuilder.DropColumn(
                name: "ProposalDigest",
                table: "HealingRepairResults");

            migrationBuilder.DropColumn(
                name: "ProposalId",
                table: "HealingRepairResults");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "HealingProviderOperations");

            migrationBuilder.DropColumn(
                name: "WebhookSecretReference",
                table: "HealingProviderConnections");

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ProviderConnectionId_IdempotencyKey",
                table: "HealingProviderOperations",
                columns: new[] { "WorkspaceId", "ProviderConnectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_Digest",
                table: "HealingEvidenceBundles",
                columns: new[] { "WorkspaceId", "Digest" },
                unique: true);
        }
    }
}
