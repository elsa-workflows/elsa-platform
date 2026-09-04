using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnforceManagedInstanceCommercialGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity",
                table: "AzureProviderOperations");

            migrationBuilder.AddColumn<int>(
                name: "MaxInstances",
                table: "OrganizationEntitlementSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: int.MaxValue);

            migrationBuilder.AddColumn<Guid>(
                name: "InstanceId",
                table: "AzureProviderOperations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "AzureProviderOperations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleAction",
                table: "AzureProviderOperations",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'EntitlementHeld', 'Running', 'RecoveryRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey", "OperationIdentity" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'EntitlementHeld', 'Running', 'RecoveryRequired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "MaxInstances",
                table: "OrganizationEntitlementSnapshots");

            migrationBuilder.DropColumn(
                name: "InstanceId",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AzureProviderOperations");

            migrationBuilder.DropColumn(
                name: "LifecycleAction",
                table: "AzureProviderOperations");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'Running', 'RecoveryRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_AzureProviderOperations_WorkspaceId_TargetKey_OperationIdentity",
                table: "AzureProviderOperations",
                columns: new[] { "WorkspaceId", "TargetKey", "OperationIdentity" },
                unique: true,
                filter: "Status IN ('Accepted', 'Queued', 'Running', 'RecoveryRequired')");
        }
    }
}
