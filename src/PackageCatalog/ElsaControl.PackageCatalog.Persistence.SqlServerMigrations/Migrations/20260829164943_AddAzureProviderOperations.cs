using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AzureProviderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ElsaVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReleaseLine = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Topology = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Isolation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ImageRepository = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ImageDigest = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: false),
                    ReleaseManifestDigest = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: true),
                    ReleaseManifestSignatureDigest = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CheckpointSequence = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ResourceGroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FoundationDeploymentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    WorkloadDeploymentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    WorkloadResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    WorkloadRevisionName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StableTrafficRevisionName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Health = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    HeartbeatAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureProviderOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AzureProviderOperationTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureProviderOperationTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AzureProviderOperationTransitions_AzureProviderOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AzureProviderOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_Status_LeaseExpiresAt_UpdatedAt",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "Status", "LeaseExpiresAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_CreatedAt",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_IdempotencyKey",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey", "OperationIdentity" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'Running', 'RecoveryRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperationTransitions_OperationId_OccurredAt",
                table: "AzureProviderOperationTransitions",
                columns: new[] { "OperationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperationTransitions_OperationId_Sequence",
                table: "AzureProviderOperationTransitions",
                columns: new[] { "OperationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AzureProviderOperationTransitions");

            migrationBuilder.DropTable(
                name: "AzureProviderOperations");
        }
    }
}
