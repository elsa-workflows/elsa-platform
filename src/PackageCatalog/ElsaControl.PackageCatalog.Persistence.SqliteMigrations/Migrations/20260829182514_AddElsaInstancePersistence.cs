using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstancePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ElsaInstanceId",
                table: "DeploymentEnvironments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ElsaInstanceId",
                table: "DeploymentRuns",
                type: "TEXT",
                nullable: true);

            // SQLite rebuilds tables for AddUniqueConstraint. Doing that while
            // DeploymentEnvironments also gains a composite FK makes a
            // temporary table reference the old, not-yet-unique parent. A unique
            // index is an equivalent SQLite parent key and avoids that rebuild.
            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OrganizationId_Id",
                table: "Workspaces",
                columns: new[] { "OrganizationId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentApplications_WorkspaceId_Id",
                table: "DeploymentApplications",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "ElsaInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DistributionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReleaseLine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestedVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PatchUpdates = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MinorUpdates = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MajorMigrations = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TopologyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FeaturePresetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FeatureOverridesJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    PackagePolicy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ConfigurationShapeRevisionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetMode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RegionCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsolationProfile = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapacityProfile = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NetworkOutcome = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DomainOutcome = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DesiredLifecycle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ObservedLifecycle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Health = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DesiredStateRevisionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedPlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedPlanSchemaVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ResolvedPlanContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    ResolvedPlanUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CurrentReleaseDistributionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentReleaseLine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentReleaseVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentReleaseManifestDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    CurrentReleaseComponentDigestsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    CurrentDeploymentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentDeploymentRevisionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentDeploymentEndpointUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PlacementAssignmentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ElsaTenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ElsaTenantAudience = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastOperationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstances", x => x.Id);
                    table.UniqueConstraint("AK_ElsaInstances_OrganizationId_WorkspaceId_Id", x => new { x.OrganizationId, x.WorkspaceId, x.Id });
                    table.UniqueConstraint("AK_ElsaInstances_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_ElsaInstances_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstances_Workspaces_OrganizationId_WorkspaceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId },
                        principalTable: "Workspaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElsaInstanceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorSubject = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeploymentRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PriorState = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NewState = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DesiredStateRevisionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlanReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DiagnosticCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RequestKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceAuditEvents_ElsaInstances_OrganizationId_WorkspaceId_InstanceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId, x.InstanceId },
                        principalTable: "ElsaInstances",
                        principalColumns: new[] { "OrganizationId", "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceAuditEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceAuditEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElsaInstanceIdentityBindings",
                columns: table => new
                {
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CanonicalCallbackUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    VerifiedEndpointOrigin = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    BindingVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceIdentityBindings", x => x.InstanceId);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceIdentityBindings_ElsaInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ElsaInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElsaInstanceMigrations",
                columns: table => new
                {
                    MigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourcePlanUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceReleaseLine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceManifestDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    SourceDeploymentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetPlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetPlanUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    TargetReleaseLine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetManifestDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    TargetDeploymentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceAccessMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CutoverAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceRetainUntil = table.Column<long>(type: "INTEGER", nullable: true),
                    EarlyReleaseApprovedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EarlyReleaseApprovedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceReleasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceMigrations", x => x.MigrationId);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceMigrations_ElsaInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ElsaInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElsaInstanceOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyScope = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpectedVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    WorkerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LeaseTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    HeartbeatAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DesiredStateRevisionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedPlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DeploymentRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FailureSummary = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceOperations_ElsaInstances_OrganizationId_WorkspaceId_InstanceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId, x.InstanceId },
                        principalTable: "ElsaInstances",
                        principalColumns: new[] { "OrganizationId", "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceOperations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceOperations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns",
                columns: new[] { "WorkspaceId", "EnvironmentId" },
                unique: true,
                filter: "ElsaInstanceId IS NOT NULL AND Status IN ('Queued', 'Running', 'RecoveryRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_ElsaInstanceId",
                table: "DeploymentEnvironments",
                column: "ElsaInstanceId",
                unique: true,
                filter: "ElsaInstanceId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironments_WorkspaceId_ElsaInstanceId",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "ElsaInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceAuditEvents_InstanceId_Sequence",
                table: "ElsaInstanceAuditEvents",
                columns: new[] { "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceAuditEvents_OrganizationId_WorkspaceId_InstanceId",
                table: "ElsaInstanceAuditEvents",
                columns: new[] { "OrganizationId", "WorkspaceId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceAuditEvents_WorkspaceId_OccurredAt",
                table: "ElsaInstanceAuditEvents",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceIdentityBindings_Audience",
                table: "ElsaInstanceIdentityBindings",
                column: "Audience",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceIdentityBindings_CanonicalCallbackUri",
                table: "ElsaInstanceIdentityBindings",
                column: "CanonicalCallbackUri",
                unique: true,
                filter: "CanonicalCallbackUri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId_Phase",
                table: "ElsaInstanceMigrations",
                columns: new[] { "InstanceId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceMigrations_InstanceId_SourceRetainUntil",
                table: "ElsaInstanceMigrations",
                columns: new[] { "InstanceId", "SourceRetainUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_ActiveInstanceId",
                table: "ElsaInstanceOperations",
                column: "InstanceId",
                unique: true,
                filter: "InstanceId IS NOT NULL AND State IN ('Accepted', 'Queued', 'Running', 'RecoveryRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_OrganizationId_WorkspaceId_InstanceId",
                table: "ElsaInstanceOperations",
                columns: new[] { "OrganizationId", "WorkspaceId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_WaitingInstanceId",
                table: "ElsaInstanceOperations",
                columns: new[] { "InstanceId", "State" },
                unique: true,
                filter: "InstanceId IS NOT NULL AND State = 'WaitingForPriorOperation'");

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_IdempotencyScope_IdempotencyKey",
                table: "ElsaInstanceOperations",
                columns: new[] { "WorkspaceId", "IdempotencyScope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_State_AcceptedAt",
                table: "ElsaInstanceOperations",
                columns: new[] { "WorkspaceId", "State", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstances_WorkspaceId_Slug",
                table: "ElsaInstances",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true,
                filter: "DeletedAt IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentEnvironments_DeploymentApplications_WorkspaceId_ApplicationId",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "ApplicationId" },
                principalTable: "DeploymentApplications",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentEnvironments_ElsaInstances_WorkspaceId_ElsaInstanceId",
                table: "DeploymentEnvironments",
                columns: new[] { "WorkspaceId", "ElsaInstanceId" },
                principalTable: "ElsaInstances",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ElsaInstanceAuditEvents_AppendOnly_Update
                BEFORE UPDATE ON ElsaInstanceAuditEvents
                BEGIN
                    SELECT RAISE(ABORT, 'Elsa instance audit events are append-only.');
                END;
                CREATE TRIGGER TR_ElsaInstanceAuditEvents_AppendOnly_Delete
                BEFORE DELETE ON ElsaInstanceAuditEvents
                BEGIN
                    SELECT RAISE(ABORT, 'Elsa instance audit events are append-only.');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS TR_ElsaInstanceAuditEvents_AppendOnly_Update;
                DROP TRIGGER IF EXISTS TR_ElsaInstanceAuditEvents_AppendOnly_Delete;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentEnvironments_DeploymentApplications_WorkspaceId_ApplicationId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentEnvironments_ElsaInstances_WorkspaceId_ElsaInstanceId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropTable(
                name: "ElsaInstanceAuditEvents");

            migrationBuilder.DropTable(
                name: "ElsaInstanceIdentityBindings");

            migrationBuilder.DropTable(
                name: "ElsaInstanceMigrations");

            migrationBuilder.DropTable(
                name: "ElsaInstanceOperations");

            migrationBuilder.DropTable(
                name: "ElsaInstances");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OrganizationId_Id",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentRuns_WorkspaceId_EnvironmentId",
                table: "DeploymentRuns");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentEnvironments_ElsaInstanceId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentEnvironments_WorkspaceId_ElsaInstanceId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentApplications_WorkspaceId_Id",
                table: "DeploymentApplications");

            migrationBuilder.DropColumn(
                name: "ElsaInstanceId",
                table: "DeploymentEnvironments");

            migrationBuilder.DropColumn(
                name: "ElsaInstanceId",
                table: "DeploymentRuns");
        }
    }
}
