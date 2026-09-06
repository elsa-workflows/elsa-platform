using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureProviderRecoveryObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ObservedInstanceVersion",
                table: "ElsaInstanceRecoveryRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObservedLifecycleAttemptNumber",
                table: "ElsaInstanceRecoveryRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryObservationDigest",
                table: "ElsaInstanceRecoveryRequests",
                type: "nvarchar(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryObservationReference",
                table: "ElsaInstanceRecoveryRequests",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttemptedStep",
                table: "AzureProviderOperations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AzureProviderRecoveryObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LifecycleOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LifecycleAction = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ObservedLifecycleAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    ObservedInstanceVersion = table.Column<int>(type: "int", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderOperationIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderRequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    ProviderVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProviderCheckpointSequence = table.Column<long>(type: "bigint", nullable: false),
                    TargetKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderScopeFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResolvedPlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResolvedPlanSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ResolvedPlanUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ResolvedPlanContentHash = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: false),
                    ProviderPlanFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderTemplateFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CompletedStep = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ObservedPhase = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ObservedHealth = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostconditionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NaturalKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordDigest = table.Column<string>(type: "nvarchar(71)", maxLength: 71, nullable: false),
                    ObservedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureProviderRecoveryObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AzureProviderRecoveryObservations_AzureProviderOperations_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "AzureProviderOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AzureProviderRecoveryObservations_AzureProviderResourceAssignments_ProviderAssignmentId",
                        column: x => x.ProviderAssignmentId,
                        principalTable: "AzureProviderResourceAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AzureProviderRecoveryObservations_ElsaInstanceOperations_LifecycleOperationId",
                        column: x => x.LifecycleOperationId,
                        principalTable: "ElsaInstanceOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AzureProviderRecoveryObservations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderRecoveryObservations_LifecycleOperationId",
                table: "AzureProviderRecoveryObservations",
                column: "LifecycleOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderRecoveryObservations_ProviderAssignmentId",
                table: "AzureProviderRecoveryObservations",
                column: "ProviderAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderRecoveryObservations_ProviderOperationId",
                table: "AzureProviderRecoveryObservations",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderRecoveryObservations_WorkspaceId_LifecycleOperationId_ObservedAt",
                table: "AzureProviderRecoveryObservations",
                columns: new[] { "WorkspaceId", "LifecycleOperationId", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderRecoveryObservations_WorkspaceId_NaturalKey",
                table: "AzureProviderRecoveryObservations",
                columns: new[] { "WorkspaceId", "NaturalKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                EXEC(N'CREATE TRIGGER dbo.TR_AzureProviderRecoveryObservations_AppendOnly
                ON dbo.AzureProviderRecoveryObservations
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    THROW 51018, ''Azure provider recovery observations are append-only'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS dbo.TR_AzureProviderRecoveryObservations_AppendOnly;");
            migrationBuilder.DropTable(
                name: "AzureProviderRecoveryObservations");

            migrationBuilder.DropColumn(
                name: "ObservedInstanceVersion",
                table: "ElsaInstanceRecoveryRequests");

            migrationBuilder.DropColumn(
                name: "ObservedLifecycleAttemptNumber",
                table: "ElsaInstanceRecoveryRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryObservationDigest",
                table: "ElsaInstanceRecoveryRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryObservationReference",
                table: "ElsaInstanceRecoveryRequests");

            migrationBuilder.DropColumn(
                name: "AttemptedStep",
                table: "AzureProviderOperations");
        }
    }
}
