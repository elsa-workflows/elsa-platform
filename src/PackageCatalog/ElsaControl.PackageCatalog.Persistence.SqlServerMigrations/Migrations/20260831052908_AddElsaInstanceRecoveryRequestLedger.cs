using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddElsaInstanceRecoveryRequestLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_RecoveryIdempotencyScope_RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations");

            migrationBuilder.CreateTable(
                name: "ElsaInstanceRecoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    IdempotencyScope = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AcceptedAt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElsaInstanceRecoveryRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceRecoveryRequests_ElsaInstanceOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ElsaInstanceOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElsaInstanceRecoveryRequests_Workspaces_OrganizationId_WorkspaceId",
                        columns: x => new { x.OrganizationId, x.WorkspaceId },
                        principalTable: "Workspaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve the previously authoritative (latest) recovery envelope.
            // Older releases could persist at most one such envelope per operation.
            migrationBuilder.Sql(
                """
                INSERT INTO ElsaInstanceRecoveryRequests
                    (Id, OrganizationId, WorkspaceId, InstanceId, OperationId, AttemptNumber,
                     IdempotencyScope, IdempotencyKey, RequestHash, AcceptedAt, CreatedAt)
                SELECT NEWID(), OrganizationId, WorkspaceId, InstanceId, Id, AttemptNumber,
                       RecoveryIdempotencyScope, RecoveryIdempotencyKey, RecoveryRequestHash,
                       UpdatedAt, UpdatedAt
                FROM ElsaInstanceOperations
                WHERE InstanceId IS NOT NULL
                  AND RecoveryIdempotencyScope IS NOT NULL
                  AND RecoveryIdempotencyKey IS NOT NULL
                  AND RecoveryRequestHash IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceRecoveryRequests_OperationId_AttemptNumber",
                table: "ElsaInstanceRecoveryRequests",
                columns: new[] { "OperationId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceRecoveryRequests_OrganizationId_WorkspaceId",
                table: "ElsaInstanceRecoveryRequests",
                columns: new[] { "OrganizationId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceRecoveryRequests_WorkspaceId_IdempotencyScope_IdempotencyKey",
                table: "ElsaInstanceRecoveryRequests",
                columns: new[] { "WorkspaceId", "IdempotencyScope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                EXEC(N'CREATE TRIGGER TR_ElsaInstanceRecoveryRequests_AppendOnly
                ON ElsaInstanceRecoveryRequests
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 51015, ''Elsa instance recovery requests are append-only'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ElsaInstanceRecoveryRequests_AppendOnly;");

            migrationBuilder.DropTable(
                name: "ElsaInstanceRecoveryRequests");

            migrationBuilder.CreateIndex(
                name: "IX_ElsaInstanceOperations_WorkspaceId_RecoveryIdempotencyScope_RecoveryIdempotencyKey",
                table: "ElsaInstanceOperations",
                columns: new[] { "WorkspaceId", "RecoveryIdempotencyScope", "RecoveryIdempotencyKey" },
                unique: true,
                filter: "RecoveryIdempotencyKey IS NOT NULL");
        }
    }
}
