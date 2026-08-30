using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations;

public partial class AddProviderReconciliationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("ReconciledAt", "ElsaInstanceOperations", "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>("ReconciledHealth", "ElsaInstanceOperations", "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<int>("ReconciledInstanceVersion", "ElsaInstanceOperations", "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>("ReconciledObservedLifecycle", "ElsaInstanceOperations", "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationDiagnosticCode", "ElsaInstanceOperations", "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationEvidenceFingerprint", "ElsaInstanceOperations", "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationRetryEvidenceDigest", "ElsaInstanceOperations", "TEXT", maxLength: 71, nullable: true);
        migrationBuilder.AddColumn<string>("ReconciliationRetryEvidenceReference", "ElsaInstanceOperations", "TEXT", maxLength: 2048, nullable: true);
        migrationBuilder.AddColumn<int>("ReconciliationVersion", "ElsaInstanceOperations", "INTEGER", nullable: false, defaultValue: 0);
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
