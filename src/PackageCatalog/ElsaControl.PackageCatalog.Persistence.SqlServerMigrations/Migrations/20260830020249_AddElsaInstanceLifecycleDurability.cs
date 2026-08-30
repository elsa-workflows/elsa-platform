using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceLifecycleDurability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElsaInstanceIntentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DistributionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReleaseLine = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PatchUpdates = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MinorUpdates = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MajorMigrations = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TopologyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FeaturePresetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FeatureOverridesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 32768, nullable: false),
                    PackagePolicy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConfigurationShapeRevisionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TargetMode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RegionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsolationProfile = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CapacityProfile = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NetworkOutcome = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DomainOutcome = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DesiredLifecycle = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AuthoredAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceIntentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceIntentRevisions_ElsaInstances_OrganizationId_WorkspaceId_InstanceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId, x.InstanceId },
                        principalTable: "ElsaInstances",
                        principalColumns: new[] { "OrganizationId", "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceIntentRevisions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceIntentRevisions_Workspaces_OrganizationId_WorkspaceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId },
                        principalTable: "Workspaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElsaInstanceLifecycleOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceLifecycleOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceLifecycleOutbox_ElsaInstanceOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ElsaInstanceOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceLifecycleOutbox_ElsaInstances_OrganizationId_WorkspaceId_InstanceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId, x.InstanceId },
                        principalTable: "ElsaInstances",
                        principalColumns: new[] { "OrganizationId", "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceLifecycleOutbox_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceLifecycleOutbox_Workspaces_OrganizationId_WorkspaceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId },
                        principalTable: "Workspaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceIntentRevisions_InstanceId_RevisionNumber",
                table: "ElsaInstanceIntentRevisions",
                columns: new[] { "InstanceId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceIntentRevisions_OrganizationId_WorkspaceId_InstanceId",
                table: "ElsaInstanceIntentRevisions",
                columns: new[] { "OrganizationId", "WorkspaceId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceIntentRevisions_WorkspaceId_ContentHash",
                table: "ElsaInstanceIntentRevisions",
                columns: new[] { "WorkspaceId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceLifecycleOutbox_OperationId",
                table: "ElsaInstanceLifecycleOutbox",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceLifecycleOutbox_OrganizationId_WorkspaceId_InstanceId",
                table: "ElsaInstanceLifecycleOutbox",
                columns: new[] { "OrganizationId", "WorkspaceId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceLifecycleOutbox_WorkspaceId_CreatedAt",
                table: "ElsaInstanceLifecycleOutbox",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceLifecycleOutbox_WorkspaceId_InstanceId_CreatedAt",
                table: "ElsaInstanceLifecycleOutbox",
                columns: new[] { "WorkspaceId", "InstanceId", "CreatedAt" });

            // Dynamic EXEC keeps CREATE TRIGGER statements safe for generated
            // idempotent scripts, whose SQL Server parser compiles the whole
            // batch before migration history guards are evaluated.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceIntentRevisions_AppendOnly
                ON ElsaInstanceIntentRevisions
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51005, ''Elsa instance intent revisions are append-only'', 1;
                END;');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceLifecycleOutbox_AppendOnly
                ON ElsaInstanceLifecycleOutbox
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51006, ''Elsa instance lifecycle outbox records are append-only'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceLifecycleOutbox_AppendOnly;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceIntentRevisions_AppendOnly;");

            migrationBuilder.DropTable(
                name: "ElsaInstanceIntentRevisions");

            migrationBuilder.DropTable(
                name: "ElsaInstanceLifecycleOutbox");
        }
    }
}
