using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260831060000_PersistAzureProviderResourceReferences")]
public partial class PersistAzureProviderResourceReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ProviderScopeFingerprint", "AzureProviderOperations", "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityResourceId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityClientId", "AzureProviderOperations", "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityPrincipalId", "AzureProviderOperations", "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("KeyVaultResourceId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("KeyVaultUri", "AzureProviderOperations", "TEXT", maxLength: 2048, nullable: true);
        migrationBuilder.AddColumn<string>("SqlServerResourceId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("SqlServerFqdn", "AzureProviderOperations", "TEXT", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("ContainerAppsEnvironmentResourceId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("RegistryResourceId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("AcrPullDeploymentId", "AzureProviderOperations", "TEXT", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("AcrPullRoleAssignmentId", "AzureProviderOperations", "TEXT", maxLength: 1024, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ProviderScopeFingerprint", "AzureProviderOperations");
        migrationBuilder.DropColumn("WorkloadIdentityResourceId", "AzureProviderOperations");
        migrationBuilder.DropColumn("WorkloadIdentityClientId", "AzureProviderOperations");
        migrationBuilder.DropColumn("WorkloadIdentityPrincipalId", "AzureProviderOperations");
        migrationBuilder.DropColumn("KeyVaultResourceId", "AzureProviderOperations");
        migrationBuilder.DropColumn("KeyVaultUri", "AzureProviderOperations");
        migrationBuilder.DropColumn("SqlServerResourceId", "AzureProviderOperations");
        migrationBuilder.DropColumn("SqlServerFqdn", "AzureProviderOperations");
        migrationBuilder.DropColumn("ContainerAppsEnvironmentResourceId", "AzureProviderOperations");
        migrationBuilder.DropColumn("RegistryResourceId", "AzureProviderOperations");
        migrationBuilder.DropColumn("AcrPullDeploymentId", "AzureProviderOperations");
        migrationBuilder.DropColumn("AcrPullRoleAssignmentId", "AzureProviderOperations");
    }
}
