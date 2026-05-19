using Elsa.Catalog.Core.Manifests;
using Elsa.Catalog.Core.Accounts;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Core.RuntimeConfigurations;
using Elsa.Catalog.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Catalog.Persistence.EntityFrameworkCore;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<PackageSource> PackageSources => Set<PackageSource>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageVersion> PackageVersions => Set<PackageVersion>();
    public DbSet<FeatureRecord> Features => Set<FeatureRecord>();
    public DbSet<FeatureSettingRecord> FeatureSettings => Set<FeatureSettingRecord>();
    public DbSet<ManifestValidationResultRecord> ManifestValidationResults => Set<ManifestValidationResultRecord>();
    public DbSet<ApprovalRecord> ApprovalRecords => Set<ApprovalRecord>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncRunItem> SyncRunItems => Set<SyncRunItem>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceEntitlementSnapshot> WorkspaceEntitlementSnapshots => Set<WorkspaceEntitlementSnapshot>();
    public DbSet<RuntimeConfiguration> RuntimeConfigurations => Set<RuntimeConfiguration>();
    public DbSet<RuntimeConfigurationVersion> RuntimeConfigurationVersions => Set<RuntimeConfigurationVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Models.PackageSourceConfiguration());
        modelBuilder.ApplyConfiguration(new Models.PackageConfiguration());
        modelBuilder.ApplyConfiguration(new Models.PackageVersionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.FeatureRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.FeatureSettingRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ManifestValidationResultRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ApprovalRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.SyncRunConfiguration());
        modelBuilder.ApplyConfiguration(new Models.SyncRunItemConfiguration());
        modelBuilder.ApplyConfiguration(new Models.AccountConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ExternalIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceEntitlementSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeConfigurationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeConfigurationVersionConfiguration());
    }
}
