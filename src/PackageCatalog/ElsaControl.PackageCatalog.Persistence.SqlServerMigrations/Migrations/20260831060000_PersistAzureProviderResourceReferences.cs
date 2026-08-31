using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260831060000_PersistAzureProviderResourceReferences")]
public partial class PersistAzureProviderResourceReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ProviderScopeFingerprint", "AzureProviderOperations", "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityResourceId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityClientId", "AzureProviderOperations", "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("WorkloadIdentityPrincipalId", "AzureProviderOperations", "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("KeyVaultResourceId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("KeyVaultUri", "AzureProviderOperations", "nvarchar(2048)", maxLength: 2048, nullable: true);
        migrationBuilder.AddColumn<string>("SqlServerResourceId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("SqlServerFqdn", "AzureProviderOperations", "nvarchar(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("ContainerAppsEnvironmentResourceId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("RegistryResourceId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>("AcrPullDeploymentId", "AzureProviderOperations", "nvarchar(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("AcrPullRoleAssignmentId", "AzureProviderOperations", "nvarchar(1024)", maxLength: 1024, nullable: true);
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
