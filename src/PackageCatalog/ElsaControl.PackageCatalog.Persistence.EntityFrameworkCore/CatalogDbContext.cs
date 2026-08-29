using ElsaControl.PackageCatalog.Core.Manifests;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using ElsaControl.PackageCatalog.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

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
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<OrganizationEntitlementSnapshot> OrganizationEntitlementSnapshots => Set<OrganizationEntitlementSnapshot>();
    public DbSet<OrganizationAuditRecord> OrganizationAuditRecords => Set<OrganizationAuditRecord>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceEntitlementSnapshot> WorkspaceEntitlementSnapshots => Set<WorkspaceEntitlementSnapshot>();
    public DbSet<RuntimeConfiguration> RuntimeConfigurations => Set<RuntimeConfiguration>();
    public DbSet<RuntimeConfigurationVersion> RuntimeConfigurationVersions => Set<RuntimeConfigurationVersion>();
    internal DbSet<Models.DeploymentApplicationEntity> DeploymentApplications => Set<Models.DeploymentApplicationEntity>();
    internal DbSet<Models.DeploymentEnvironmentEntity> DeploymentEnvironments => Set<Models.DeploymentEnvironmentEntity>();
    internal DbSet<Models.DeploymentTierDefinitionEntity> DeploymentTierDefinitions => Set<Models.DeploymentTierDefinitionEntity>();
    internal DbSet<Models.DeploymentTierCapabilityAssignmentEntity> DeploymentTierCapabilityAssignments => Set<Models.DeploymentTierCapabilityAssignmentEntity>();
    internal DbSet<Models.DeploymentTierChangeRecordEntity> DeploymentTierChangeRecords => Set<Models.DeploymentTierChangeRecordEntity>();
    internal DbSet<Models.WorkflowEngineEntity> WorkflowEngines => Set<Models.WorkflowEngineEntity>();
    internal DbSet<Models.DeploymentSecretStoreEntity> DeploymentSecretStores => Set<Models.DeploymentSecretStoreEntity>();
    internal DbSet<Models.DeploymentCredentialReferenceEntity> DeploymentCredentialReferences => Set<Models.DeploymentCredentialReferenceEntity>();
    internal DbSet<Models.WorkspaceDeploymentArtifactEntity> WorkspaceDeploymentArtifacts => Set<Models.WorkspaceDeploymentArtifactEntity>();
    internal DbSet<Models.WorkspaceArtifactUploadSessionEntity> WorkspaceArtifactUploadSessions => Set<Models.WorkspaceArtifactUploadSessionEntity>();
    internal DbSet<Models.EngineCapabilityEntity> EngineCapabilities => Set<Models.EngineCapabilityEntity>();
    internal DbSet<Models.RuntimeControlEntity> RuntimeControls => Set<Models.RuntimeControlEntity>();
    internal DbSet<Models.RuntimeControlExecutionEntity> RuntimeControlExecutions => Set<Models.RuntimeControlExecutionEntity>();
    internal DbSet<Models.DesiredStateRevisionEntity> DesiredStateRevisions => Set<Models.DesiredStateRevisionEntity>();
    internal DbSet<Models.StructuredDesiredStateRecordEntity> StructuredDesiredStateRecords => Set<Models.StructuredDesiredStateRecordEntity>();
    internal DbSet<Models.WorkspacePermissionGrantEntity> WorkspacePermissionGrants => Set<Models.WorkspacePermissionGrantEntity>();
    internal DbSet<Models.WorkspacePermissionAuditRecordEntity> WorkspacePermissionAuditRecords => Set<Models.WorkspacePermissionAuditRecordEntity>();
    internal DbSet<Models.ActionConfirmationEntity> ActionConfirmations => Set<Models.ActionConfirmationEntity>();
    internal DbSet<Models.DeploymentRunEntity> DeploymentRuns => Set<Models.DeploymentRunEntity>();
    internal DbSet<Models.DeploymentRunHistoryEventEntity> DeploymentRunHistoryEvents => Set<Models.DeploymentRunHistoryEventEntity>();
    internal DbSet<Models.DeploymentCommandEntity> DeploymentCommands => Set<Models.DeploymentCommandEntity>();
    internal DbSet<Models.DeploymentCommandEventEntity> DeploymentCommandEvents => Set<Models.DeploymentCommandEventEntity>();
    internal DbSet<Models.AzureProviderOperationEntity> AzureProviderOperations => Set<Models.AzureProviderOperationEntity>();
    internal DbSet<Models.AzureProviderOperationTransitionEntity> AzureProviderOperationTransitions => Set<Models.AzureProviderOperationTransitionEntity>();
    internal DbSet<Models.DeploymentCommandWebhookNotificationEntity> DeploymentCommandWebhookNotifications => Set<Models.DeploymentCommandWebhookNotificationEntity>();
    internal DbSet<Models.ObservabilityBindingEntity> ObservabilityBindings => Set<Models.ObservabilityBindingEntity>();
    internal DbSet<Models.DriftReportItemEntity> DriftReportItems => Set<Models.DriftReportItemEntity>();
    internal DbSet<Models.WeaverSessionEntity> WeaverSessions => Set<Models.WeaverSessionEntity>();
    internal DbSet<Models.WeaverMessageEntity> WeaverMessages => Set<Models.WeaverMessageEntity>();
    internal DbSet<Models.WeaverToolCallEntity> WeaverToolCalls => Set<Models.WeaverToolCallEntity>();
    internal DbSet<Models.WeaverPlanEntity> WeaverPlans => Set<Models.WeaverPlanEntity>();
    internal DbSet<Models.WeaverPlanApprovalEntity> WeaverPlanApprovals => Set<Models.WeaverPlanApprovalEntity>();
    internal DbSet<Models.WeaverPlanExecutionEntity> WeaverPlanExecutions => Set<Models.WeaverPlanExecutionEntity>();

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
        modelBuilder.ApplyConfiguration(new Models.OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.OrganizationMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new Models.OrganizationEntitlementSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new Models.OrganizationAuditRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceEntitlementSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeConfigurationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeConfigurationVersionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentEnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentTierDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentTierCapabilityAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentTierChangeRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkflowEngineConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentSecretStoreConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentCredentialReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceDeploymentArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspaceArtifactUploadSessionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.EngineCapabilityConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeControlConfiguration());
        modelBuilder.ApplyConfiguration(new Models.RuntimeControlExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DesiredStateRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.StructuredDesiredStateRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspacePermissionGrantConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WorkspacePermissionAuditRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ActionConfirmationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentRunConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentRunHistoryEventConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentCommandConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentCommandEventConfiguration());
        modelBuilder.ApplyConfiguration(new Models.AzureProviderOperationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.AzureProviderOperationTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DeploymentCommandWebhookNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ObservabilityBindingConfiguration());
        modelBuilder.ApplyConfiguration(new Models.DriftReportItemConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverSessionConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverMessageConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverToolCallConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverPlanConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverPlanApprovalConfiguration());
        modelBuilder.ApplyConfiguration(new Models.WeaverPlanExecutionConfiguration());
    }

    public override int SaveChanges()
    {
        EnsureWorkspacePermissionAuditIsAppendOnly();
        EnsureAzureOperationTransitionsAreAppendOnly();
        EnsureOrganizationsForNewWorkspaces();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkspacePermissionAuditIsAppendOnly();
        EnsureAzureOperationTransitionsAreAppendOnly();
        EnsureOrganizationsForNewWorkspaces();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnsureWorkspacePermissionAuditIsAppendOnly()
    {
        var mutatedAuditRecord = ChangeTracker.Entries<Models.WorkspacePermissionAuditRecordEntity>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (mutatedAuditRecord is not null)
            throw new InvalidOperationException("Workspace permission audit records are append-only.");
    }

    private void EnsureAzureOperationTransitionsAreAppendOnly()
    {
        var mutatedTransition = ChangeTracker.Entries<Models.AzureProviderOperationTransitionEntity>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (mutatedTransition is not null)
            throw new InvalidOperationException("Azure provider operation transitions are append-only.");
    }

    private void EnsureOrganizationsForNewWorkspaces()
    {
        var workspaceEntries = ChangeTracker.Entries<Workspace>()
            .Where(x => x.State == EntityState.Added)
            .ToList();

        foreach (var entry in workspaceEntries)
        {
            if (entry.Entity.Organization is not null || entry.Entity.OrganizationId != Guid.Empty)
                continue;

            var organizationId = Guid.NewGuid();
            entry.Entity.OrganizationId = organizationId;
            Organizations.Add(new Organization
            {
                Id = organizationId,
                Name = string.IsNullOrWhiteSpace(entry.Entity.Name) ? "Workspace Organization" : entry.Entity.Name
            });
        }
    }
}
