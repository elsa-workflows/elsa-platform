using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations;

public partial class AddProviderReconciliationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("ReconciledAt", "ElsaInstanceOperations", "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("ReconciledHealth", "ElsaInstanceOperations", "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<int>("ReconciledInstanceVersion", "ElsaInstanceOperations", "int", nullable: true);
        migrationBuilder.AddColumn<string>("ReconciledObservedLifecycle", "ElsaInstanceOperations", "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationDiagnosticCode", "ElsaInstanceOperations", "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationEvidenceFingerprint", "ElsaInstanceOperations", "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationRetryEvidenceDigest", "ElsaInstanceOperations", "nvarchar(71)", maxLength: 71, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationRetryEvidenceReference", "ElsaInstanceOperations", "nvarchar(2048)", maxLength: 2048, nullable: true);
        migrationBuilder.AddColumn<int>("ReconciliationVersion", "ElsaInstanceOperations", "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReconciledAt", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciledHealth", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciledInstanceVersion", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciledObservedLifecycle", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciliationDiagnosticCode", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciliationEvidenceFingerprint", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciliationRetryEvidenceDigest", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciliationRetryEvidenceReference", "ElsaInstanceOperations");
        migrationBuilder.DropColumn("ReconciliationVersion", "ElsaInstanceOperations");
    }
}
