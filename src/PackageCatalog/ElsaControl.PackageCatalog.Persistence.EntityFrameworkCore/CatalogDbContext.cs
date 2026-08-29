using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.PackageCatalog.Core.Manifests;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using ElsaControl.PackageCatalog.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    internal DbSet<Models.ElsaInstanceEntity> ElsaInstances => Set<Models.ElsaInstanceEntity>();
    internal DbSet<Models.ElsaInstanceOperationEntity> ElsaInstanceOperations => Set<Models.ElsaInstanceOperationEntity>();
    internal DbSet<Models.ElsaInstanceAuditEventEntity> ElsaInstanceAuditEvents => Set<Models.ElsaInstanceAuditEventEntity>();
    internal DbSet<Models.ElsaInstanceIdentityBindingEntity> ElsaInstanceIdentityBindings => Set<Models.ElsaInstanceIdentityBindingEntity>();
    internal DbSet<Models.ElsaInstanceMigrationEntity> ElsaInstanceMigrations => Set<Models.ElsaInstanceMigrationEntity>();
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
        modelBuilder.ApplyConfiguration(new Models.ElsaInstanceConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ElsaInstanceOperationConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ElsaInstanceAuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ElsaInstanceIdentityBindingConfiguration());
        modelBuilder.ApplyConfiguration(new Models.ElsaInstanceMigrationConfiguration());
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

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareForSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareForSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareForSave()
    {
        EnsureWorkspacePermissionAuditIsAppendOnly();
        EnsureElsaInstanceAuditIsAppendOnly();
        EnsureElsaInstanceDurableRowsAreNotDeleted();
        ValidateElsaInstancePersistence();
        EnsureOrganizationsForNewWorkspaces();
    }

    private void EnsureWorkspacePermissionAuditIsAppendOnly()
    {
        var mutatedAuditRecord = ChangeTracker.Entries<Models.WorkspacePermissionAuditRecordEntity>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (mutatedAuditRecord is not null)
            throw new InvalidOperationException("Workspace permission audit records are append-only.");
    }

    private void EnsureElsaInstanceAuditIsAppendOnly()
    {
        var mutatedAuditRecord = ChangeTracker.Entries<Models.ElsaInstanceAuditEventEntity>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (mutatedAuditRecord is not null)
            throw new InvalidOperationException("Elsa instance audit events are append-only.");
    }

    private void EnsureElsaInstanceDurableRowsAreNotDeleted()
    {
        if (ChangeTracker.Entries<Models.ElsaInstanceEntity>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("Elsa instances are tombstoned and cannot be deleted.");
        if (ChangeTracker.Entries<Models.ElsaInstanceMigrationEntity>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("Elsa instance migrations are durable and cannot be deleted.");
    }

    private void ValidateElsaInstancePersistence()
    {
        foreach (var entry in ChangeTracker.Entries<Models.ElsaInstanceEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var instance = entry.Entity;
            if (instance.Id == Guid.Empty || instance.OrganizationId == Guid.Empty || instance.WorkspaceId == Guid.Empty)
                throw new InvalidOperationException("An Elsa instance requires stable ownership identifiers.");
            RequireDisplayName(instance.Name, nameof(instance.Name), 256);
            instance.Slug = RequireSlug(instance.Slug);
            if (entry.State == EntityState.Modified &&
                !string.Equals(instance.Slug, entry.Property(x => x.Slug).OriginalValue, StringComparison.Ordinal))
                throw new InvalidOperationException("An Elsa instance slug is immutable.");
            instance.DistributionId = RequireCatalogValue(instance.DistributionId, nameof(instance.DistributionId));
            instance.ReleaseLine = RequireCatalogValue(instance.ReleaseLine, nameof(instance.ReleaseLine));
            instance.RequestedVersion = OptionalCatalogValue(instance.RequestedVersion, nameof(instance.RequestedVersion));
            if (instance.RequestedVersion is not null && !BelongsToReleaseLine(instance.ReleaseLine, instance.RequestedVersion))
                throw new InvalidOperationException("Requested version must belong to the selected release line.");
            instance.Channel = RequireCatalogValue(instance.Channel, nameof(instance.Channel));
            instance.PatchUpdates = RequireCatalogValue(instance.PatchUpdates, nameof(instance.PatchUpdates));
            instance.MinorUpdates = RequireCatalogValue(instance.MinorUpdates, nameof(instance.MinorUpdates));
            instance.MajorMigrations = RequireCatalogValue(instance.MajorMigrations, nameof(instance.MajorMigrations));
            instance.TopologyId = RequireCatalogValue(instance.TopologyId, nameof(instance.TopologyId));
            instance.FeaturePresetId = OptionalCatalogValue(instance.FeaturePresetId, nameof(instance.FeaturePresetId));
            ValidateFeatureOverrides(instance.FeatureOverridesJson);
            instance.PackagePolicy = OptionalCatalogValue(instance.PackagePolicy, nameof(instance.PackagePolicy));
            instance.ConfigurationShapeRevisionId = OptionalCatalogValue(instance.ConfigurationShapeRevisionId, nameof(instance.ConfigurationShapeRevisionId));
            instance.TargetMode = RequireCatalogValue(instance.TargetMode, nameof(instance.TargetMode));
            instance.RegionCode = RequireCatalogValue(instance.RegionCode, nameof(instance.RegionCode));
            instance.IsolationProfile = RequireCatalogValue(instance.IsolationProfile, nameof(instance.IsolationProfile));
            instance.CapacityProfile = RequireCatalogValue(instance.CapacityProfile, nameof(instance.CapacityProfile));
            instance.NetworkOutcome = RequireCatalogValue(instance.NetworkOutcome, nameof(instance.NetworkOutcome));
            instance.DomainOutcome = RequireCatalogValue(instance.DomainOutcome, nameof(instance.DomainOutcome));
            instance.DesiredStateRevisionId = OptionalSafeReference(instance.DesiredStateRevisionId, nameof(instance.DesiredStateRevisionId), 128);
            var hasPlan = !string.IsNullOrWhiteSpace(instance.ResolvedPlanId);
            instance.ResolvedPlanId = OptionalSafeReference(instance.ResolvedPlanId, nameof(instance.ResolvedPlanId), 128);
            instance.ResolvedPlanContentHash = OptionalSha256Digest(instance.ResolvedPlanContentHash, nameof(instance.ResolvedPlanContentHash));
            instance.ResolvedPlanUri = OptionalPlanUri(instance.ResolvedPlanUri, nameof(instance.ResolvedPlanUri));
            var planValues = new object?[]
            {
                instance.ResolvedPlanSchemaVersion,
                instance.ResolvedPlanContentHash,
                instance.ResolvedPlanUri
            };
            if (!hasPlan && planValues.Any(x => x is not null))
                throw new InvalidOperationException("Resolved plan fields must be persisted together.");
            if (hasPlan && (instance.ResolvedPlanSchemaVersion is null or < 1 ||
                            string.IsNullOrWhiteSpace(instance.ResolvedPlanContentHash) ||
                            string.IsNullOrWhiteSpace(instance.ResolvedPlanUri)))
                throw new InvalidOperationException("Resolved plan fields must be persisted together.");
            if (hasPlan)
                RequireInstancePlanUri(instance.ResolvedPlanUri!, nameof(instance.ResolvedPlanUri), instance.WorkspaceId, instance.Id, instance.ResolvedPlanId!);

            instance.CurrentReleaseDistributionId = OptionalCatalogValue(instance.CurrentReleaseDistributionId, nameof(instance.CurrentReleaseDistributionId));
            instance.CurrentReleaseLine = OptionalCatalogValue(instance.CurrentReleaseLine, nameof(instance.CurrentReleaseLine));
            instance.CurrentReleaseVersion = OptionalCatalogValue(instance.CurrentReleaseVersion, nameof(instance.CurrentReleaseVersion));
            instance.CurrentReleaseManifestDigest = OptionalSha256Digest(instance.CurrentReleaseManifestDigest, nameof(instance.CurrentReleaseManifestDigest));
            var currentRelease = new string?[]
            {
                instance.CurrentReleaseDistributionId,
                instance.CurrentReleaseLine,
                instance.CurrentReleaseVersion,
                instance.CurrentReleaseManifestDigest,
                instance.CurrentReleaseComponentDigestsJson
            };
            var hasCurrentRelease = currentRelease.Any(x => !string.IsNullOrWhiteSpace(x));
            if (hasCurrentRelease && (!hasPlan || currentRelease.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidOperationException("Current resolved release fields must be persisted together.");
            if (hasCurrentRelease && !BelongsToReleaseLine(instance.CurrentReleaseLine!, instance.CurrentReleaseVersion!))
                throw new InvalidOperationException("Current release version must belong to the selected release line.");
            ValidateComponentDigests(instance.CurrentReleaseComponentDigestsJson);
            instance.CurrentDeploymentId = OptionalSafeReference(instance.CurrentDeploymentId, nameof(instance.CurrentDeploymentId), 128);
            instance.CurrentDeploymentRevisionId = OptionalSafeReference(instance.CurrentDeploymentRevisionId, nameof(instance.CurrentDeploymentRevisionId), 128);
            instance.CurrentDeploymentEndpointUri = OptionalEndpointUri(instance.CurrentDeploymentEndpointUri, nameof(instance.CurrentDeploymentEndpointUri));
            instance.PlacementAssignmentId = OptionalSafeReference(instance.PlacementAssignmentId, nameof(instance.PlacementAssignmentId), 128);
            instance.ElsaTenantId = OptionalSafeReference(instance.ElsaTenantId, nameof(instance.ElsaTenantId), 128);
            var tenantAudience = OptionalAudience(instance.ElsaTenantAudience, nameof(instance.ElsaTenantAudience));
            if (tenantAudience is not null)
            {
                var expectedAudience = $"urn:elsa:instance:{instance.Id:D}".ToLowerInvariant();
                instance.ElsaTenantAudience = RequireExactAudience(tenantAudience, expectedAudience);
            }
            instance.LastOperationId = OptionalSafeReference(instance.LastOperationId, nameof(instance.LastOperationId), 128);

            if (instance.DeletedAt is null && instance.ObservedLifecycle == ElsaObservedLifecycle.Deleted)
                throw new InvalidOperationException("A deleted instance requires a tombstone timestamp.");
            if (instance.DeletedAt is not null &&
                (instance.ObservedLifecycle != ElsaObservedLifecycle.Deleted || instance.DesiredLifecycle != ElsaDesiredLifecycle.Deleting))
                throw new InvalidOperationException("Only a deleting instance can carry a deleted tombstone.");

            EnsureDefined(instance.DesiredLifecycle, nameof(instance.DesiredLifecycle));
            EnsureDefined(instance.ObservedLifecycle, nameof(instance.ObservedLifecycle));
            EnsureDefined(instance.Health, nameof(instance.Health));
            if (entry.State == EntityState.Added && instance.Version < 1)
                instance.Version = 1;
            if (entry.State == EntityState.Modified)
            {
                var originalVersion = entry.Property(x => x.Version).OriginalValue;
                instance.Version = checked(originalVersion + 1);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Models.ElsaInstanceOperationEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var operation = entry.Entity;
            EnsureDefined(operation.Action, nameof(operation.Action));
            EnsureDefined(operation.State, nameof(operation.State));
            if (operation.Id == Guid.Empty || operation.OrganizationId == Guid.Empty || operation.WorkspaceId == Guid.Empty)
                throw new InvalidOperationException("An instance operation requires stable ownership identifiers.");
            operation.IdempotencyScope = RequireSafeReference(operation.IdempotencyScope, nameof(operation.IdempotencyScope), 256);
            operation.IdempotencyKey = RequireSafeToken(operation.IdempotencyKey, nameof(operation.IdempotencyKey), 128);
            operation.RequestHash = RequireCanonicalHash(operation.RequestHash, nameof(operation.RequestHash));
            operation.WorkerId = OptionalSafeToken(operation.WorkerId, nameof(operation.WorkerId), 256);
            operation.LeaseTokenHash = OptionalCanonicalHash(operation.LeaseTokenHash, nameof(operation.LeaseTokenHash));
            operation.DesiredStateRevisionId = OptionalSafeReference(operation.DesiredStateRevisionId, nameof(operation.DesiredStateRevisionId), 128);
            operation.ResolvedPlanId = OptionalSafeReference(operation.ResolvedPlanId, nameof(operation.ResolvedPlanId), 128);
            operation.FailureCode = OptionalSafeCode(operation.FailureCode, nameof(operation.FailureCode));
            operation.FailureSummary = operation.FailureCode ??
                (string.IsNullOrWhiteSpace(operation.FailureSummary) ? null : "operation.failure");
            if (operation.ExpectedVersion < 1 || operation.AttemptNumber < 1)
                throw new InvalidOperationException("An instance operation requires positive version and attempt values.");
            if (operation.InstanceId is not null && operation.InstanceId == Guid.Empty)
                throw new InvalidOperationException("An instance operation requires organization ownership.");
            if (operation.InstanceId is null && operation.Action != ElsaInstanceOperationAction.Create)
                throw new InvalidOperationException("Only create operations may omit an instance ID.");
            if (operation.State == ElsaInstanceOperationState.WaitingForPriorOperation &&
                operation.Action != ElsaInstanceOperationAction.Delete)
                throw new InvalidOperationException("Only delete operations may wait for a prior operation.");

            if (entry.State == EntityState.Modified)
            {
                foreach (var property in new[]
                         {
                             nameof(Models.ElsaInstanceOperationEntity.InstanceId),
                             nameof(Models.ElsaInstanceOperationEntity.OrganizationId),
                             nameof(Models.ElsaInstanceOperationEntity.WorkspaceId),
                             nameof(Models.ElsaInstanceOperationEntity.Action),
                             nameof(Models.ElsaInstanceOperationEntity.IdempotencyScope),
                             nameof(Models.ElsaInstanceOperationEntity.IdempotencyKey),
                             nameof(Models.ElsaInstanceOperationEntity.RequestHash),
                             nameof(Models.ElsaInstanceOperationEntity.ExpectedVersion),
                             nameof(Models.ElsaInstanceOperationEntity.AcceptedAt),
                             nameof(Models.ElsaInstanceOperationEntity.CreatedAt)
                         })
                    EnsureUnchanged(entry, property, entry.Property(property).CurrentValue,
                        "Instance operation envelope fields are immutable.");

                var originalState = (ElsaInstanceOperationState)entry.Property(nameof(Models.ElsaInstanceOperationEntity.State)).OriginalValue!;
                EnsureDefined(originalState, nameof(Models.ElsaInstanceOperationEntity.State));
                if (!ElsaInstanceOperation.CanTransition(originalState, operation.State))
                    throw new InvalidOperationException("Instance operation state transition is not allowed.");
                var originalAttemptNumber = (int)entry.Property(nameof(Models.ElsaInstanceOperationEntity.AttemptNumber)).OriginalValue!;
                if (operation.AttemptNumber < originalAttemptNumber)
                    throw new InvalidOperationException("Instance operation attempt number cannot decrease.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Models.ElsaInstanceIdentityBindingEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var binding = entry.Entity;
            if (binding.InstanceId == Guid.Empty || binding.BindingVersion < 1)
                throw new InvalidOperationException("An identity binding requires stable versioned ownership.");
            var expectedAudience = $"urn:elsa:instance:{binding.InstanceId:D}".ToLowerInvariant();
            binding.Audience = RequireExactAudience(binding.Audience, expectedAudience);
            binding.VerifiedEndpointOrigin = RequireVerifiedOrigin(binding.VerifiedEndpointOrigin);
            binding.CanonicalCallbackUri = RequireCallbackUri(binding.CanonicalCallbackUri, binding.VerifiedEndpointOrigin);
            if (entry.State == EntityState.Modified)
            {
                if (binding.BindingVersion != (int)entry.Property(x => x.BindingVersion).OriginalValue + 1)
                    throw new InvalidOperationException("Identity binding versions must advance exactly one step.");
                if (binding.ChangedAt <= (DateTimeOffset)entry.Property(x => x.ChangedAt).OriginalValue)
                    throw new InvalidOperationException("Identity binding changes must be strictly later.");
                if (!string.Equals(binding.Audience, entry.Property(x => x.Audience).OriginalValue, StringComparison.Ordinal))
                    throw new InvalidOperationException("Identity binding audience is immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Models.ElsaInstanceAuditEventEntity>()
                     .Where(x => x.State == EntityState.Added))
        {
            var audit = entry.Entity;
            audit.EventType = RequireSafeCode(audit.EventType, nameof(audit.EventType));
            audit.DiagnosticCode = OptionalSafeCode(audit.DiagnosticCode, nameof(audit.DiagnosticCode));
            // Human-readable summaries and operator subjects are not durable
            // payload channels. Keep only a stable code and a one-way subject
            // fingerprint at the persistence boundary.
            audit.Summary = audit.DiagnosticCode ?? audit.EventType;
            if (!string.IsNullOrWhiteSpace(audit.OperatorSubject))
            {
                if (audit.OperatorSubject.Length > 512)
                    throw new InvalidOperationException("Operator subject exceeds the safe bound.");
                audit.OperatorSubject = NormalizeSubjectFingerprint(audit.OperatorSubject);
            }
            audit.RequestKeyHash = OptionalCanonicalHash(audit.RequestKeyHash, nameof(audit.RequestKeyHash));
            audit.PriorState = OptionalSafeCode(audit.PriorState, nameof(audit.PriorState));
            audit.NewState = OptionalSafeCode(audit.NewState, nameof(audit.NewState));
            audit.DesiredStateRevisionId = OptionalSafeReference(audit.DesiredStateRevisionId, nameof(audit.DesiredStateRevisionId), 128);
            audit.PlanReference = OptionalPlanUri(audit.PlanReference, nameof(audit.PlanReference));
        }

        foreach (var entry in ChangeTracker.Entries<Models.ElsaInstanceMigrationEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var migration = entry.Entity;
            if (migration.MigrationId == Guid.Empty || migration.InstanceId == Guid.Empty)
                throw new InvalidOperationException("An instance migration requires stable identifiers.");
            if (entry.State == EntityState.Modified)
            {
                foreach (var property in ImmutableMigrationProperties)
                    EnsureUnchanged(entry, property, entry.Property(property).CurrentValue,
                        "Instance migration identity and release references are immutable.");
            }
            migration.Phase = RequireMigrationPhase(migration.Phase);
            migration.SourceAccessMode = RequireSourceAccessMode(migration.SourceAccessMode);
            ValidateMigrationTuple(migration, source: true);
            ValidateMigrationTuple(migration, source: false);
            if (migration.CutoverAt is not null && migration.SourceRetainUntil is null)
                throw new InvalidOperationException("Migration cutover requires a source retention timestamp.");
            if (migration.SourceRetainUntil is not null && migration.CutoverAt is null)
                throw new InvalidOperationException("Migration source retention requires a cutover timestamp.");
            if (migration.CutoverAt is not null && migration.SourceRetainUntil < migration.CutoverAt.Value.AddDays(30))
                throw new InvalidOperationException("Migration source retention must be at least 30 days after cutover.");
            if (migration.SourceReleasedAt is not null && migration.SourceRetainUntil is null)
                throw new InvalidOperationException("Source release requires a retention timestamp.");
            if (migration.SourceReleasedAt < migration.CutoverAt)
                throw new InvalidOperationException("Source release cannot precede migration cutover.");
            var hasApproval = migration.EarlyReleaseApprovedByAccountId is not null || migration.EarlyReleaseApprovedAt is not null;
            if (hasApproval && (migration.EarlyReleaseApprovedByAccountId == Guid.Empty || migration.EarlyReleaseApprovedAt is null))
                throw new InvalidOperationException("Early source release approval is incomplete.");
            if (migration.SourceReleasedAt is not null && migration.SourceReleasedAt < migration.SourceRetainUntil &&
                (!hasApproval || migration.EarlyReleaseApprovedAt > migration.SourceReleasedAt))
                throw new InvalidOperationException("Early source release requires prior approval.");
            migration.SourcePlanId = OptionalSafeReference(migration.SourcePlanId, nameof(migration.SourcePlanId), 128);
            migration.SourcePlanUri = OptionalPlanUri(migration.SourcePlanUri, nameof(migration.SourcePlanUri));
            migration.SourceReleaseLine = OptionalCatalogValue(migration.SourceReleaseLine, nameof(migration.SourceReleaseLine));
            migration.SourceVersion = OptionalCatalogValue(migration.SourceVersion, nameof(migration.SourceVersion));
            migration.SourceManifestDigest = OptionalSha256Digest(migration.SourceManifestDigest, nameof(migration.SourceManifestDigest));
            migration.SourceDeploymentId = OptionalSafeReference(migration.SourceDeploymentId, nameof(migration.SourceDeploymentId), 128);
            migration.TargetPlanId = OptionalSafeReference(migration.TargetPlanId, nameof(migration.TargetPlanId), 128);
            migration.TargetPlanUri = OptionalPlanUri(migration.TargetPlanUri, nameof(migration.TargetPlanUri));
            migration.TargetReleaseLine = OptionalCatalogValue(migration.TargetReleaseLine, nameof(migration.TargetReleaseLine));
            migration.TargetVersion = OptionalCatalogValue(migration.TargetVersion, nameof(migration.TargetVersion));
            migration.TargetManifestDigest = OptionalSha256Digest(migration.TargetManifestDigest, nameof(migration.TargetManifestDigest));
            migration.TargetDeploymentId = OptionalSafeReference(migration.TargetDeploymentId, nameof(migration.TargetDeploymentId), 128);
            var instance = ChangeTracker.Entries<Models.ElsaInstanceEntity>().Select(x => x.Entity).FirstOrDefault(x => x.Id == migration.InstanceId);
            instance ??= ElsaInstances.Find(migration.InstanceId);
            if (instance is not null)
            {
                migration.OrganizationId = instance.OrganizationId;
                migration.WorkspaceId = instance.WorkspaceId;
            }
            if (migration.OrganizationId == Guid.Empty)
                migration.OrganizationId = ChangeTracker.Entries<Workspace>().Select(x => x.Entity)
                    .FirstOrDefault(x => x.Id == migration.WorkspaceId)?.OrganizationId ?? Guid.Empty;
            if (migration.OrganizationId == Guid.Empty || migration.WorkspaceId == Guid.Empty)
                throw new InvalidOperationException("Migration ownership is required.");
            RequireInstancePlanUri(migration.SourcePlanUri!, nameof(migration.SourcePlanUri), migration.WorkspaceId, migration.InstanceId, migration.SourcePlanId!);
            RequireInstancePlanUri(migration.TargetPlanUri!, nameof(migration.TargetPlanUri), migration.WorkspaceId, migration.InstanceId, migration.TargetPlanId!);

            if (entry.State == EntityState.Modified)
            {
                var originalPhase = (string)entry.Property(nameof(Models.ElsaInstanceMigrationEntity.Phase)).OriginalValue!;
                if (!CanTransitionMigrationPhase(originalPhase, migration.Phase))
                    throw new InvalidOperationException("Instance migration phase transition is not allowed.");
                var originalUpdatedAt = (DateTimeOffset)entry.Property(nameof(Models.ElsaInstanceMigrationEntity.UpdatedAt)).OriginalValue!;
                if (migration.UpdatedAt <= originalUpdatedAt)
                    throw new InvalidOperationException("Instance migration updates must advance UpdatedAt.");
            }
        }

        ValidateManagedDeploymentRunBindings();
    }

    /// <summary>
    /// A managed deployment run carries a reservation for one explicit Elsa
    /// instance.  The reservation is valid only when the referenced environment
    /// carries the same instance binding in the same workspace.  This validation
    /// intentionally leaves null legacy runs and environments alone.
    /// </summary>
    private void ValidateManagedDeploymentRunBindings()
    {
        var trackedEnvironments = ChangeTracker.Entries<Models.DeploymentEnvironmentEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);

        var trackedRunEntries = ChangeTracker.Entries<Models.DeploymentRunEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();

        foreach (var entry in trackedRunEntries)
        {
            if (entry.State == EntityState.Modified)
            {
                var originalInstanceId = (Guid?)entry.Property(nameof(Models.DeploymentRunEntity.ElsaInstanceId)).OriginalValue;
                if (originalInstanceId is not null && entry.Entity.ElsaInstanceId != originalInstanceId)
                    throw new InvalidOperationException("A managed deployment run instance binding is immutable.");
            }
        }

        var trackedRuns = trackedRunEntries.Select(entry => entry.Entity).ToArray();

        foreach (var run in trackedRuns.Where(run => run.ElsaInstanceId is not null))
        {
            var environment = trackedEnvironments.TryGetValue(run.EnvironmentId, out var changedEnvironment)
                ? changedEnvironment
                : DeploymentEnvironments.Local.FirstOrDefault(environment => environment.Id == run.EnvironmentId)
                  ?? DeploymentEnvironments.AsNoTracking().SingleOrDefault(environment => environment.Id == run.EnvironmentId);

            if (environment is null || environment.WorkspaceId != run.WorkspaceId || environment.ElsaInstanceId != run.ElsaInstanceId)
                throw new InvalidOperationException("A managed deployment run must match its environment instance binding.");
        }

        // An environment binding cannot be changed or removed while an
        // existing managed run still points at the old binding.  Exclude only
        // tracked run writes so a coordinated run/environment update is
        // validated against its final values below.
        foreach (var environment in trackedEnvironments.Values)
        {
            var changedRunIds = trackedRuns
                .Where(run => run.EnvironmentId == environment.Id)
                .Select(run => run.Id)
                .ToArray();

            var persistedRuns = DeploymentRuns.AsNoTracking()
                .Where(run => run.EnvironmentId == environment.Id && run.ElsaInstanceId != null)
                .Where(run => !changedRunIds.Contains(run.Id))
                .ToArray();

            if (persistedRuns.Any(run => run.WorkspaceId != environment.WorkspaceId || run.ElsaInstanceId != environment.ElsaInstanceId))
                throw new InvalidOperationException("An environment binding cannot break an existing managed deployment run.");

            foreach (var run in trackedRuns.Where(run => run.EnvironmentId == environment.Id && run.ElsaInstanceId is not null))
            {
                if (run.WorkspaceId != environment.WorkspaceId || run.ElsaInstanceId != environment.ElsaInstanceId)
                    throw new InvalidOperationException("A managed deployment run must match its environment instance binding.");
            }
        }
    }

    private static void EnsureDefined<TEnum>(TEnum value, string name) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new InvalidOperationException($"{name} contains an unsupported value.");
    }

    private static void EnsureUnchanged(EntityEntry entry, string propertyName, object? currentValue, string message)
    {
        if (!Equals(entry.Property(propertyName).OriginalValue, currentValue))
            throw new InvalidOperationException(message);
    }

    private static string RequireSafeCode(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ':')))
            throw new InvalidOperationException($"{name} must be a stable safe code.");
        return value;
    }

    private static string RequireMigrationPhase(string? value)
    {
        var normalized = RequireSafeCode(value, nameof(Models.ElsaInstanceMigrationEntity.Phase));
        if (normalized is not ("Planned" or "Preparing" or "ProvisioningTarget" or "Validating" or "Cutover" or "RetainingSource" or "RetiringSource" or "RolledBack" or "Released" or "Failed"))
            throw new InvalidOperationException("Migration phase is not supported.");
        return normalized;
    }

    private static string RequireSourceAccessMode(string? value)
    {
        var normalized = RequireSafeCode(value, nameof(Models.ElsaInstanceMigrationEntity.SourceAccessMode));
        if (normalized is not ("Running" or "ReadOnly" or "Stopped"))
            throw new InvalidOperationException("Migration source access mode is not supported.");
        return normalized;
    }

    private static bool CanTransitionMigrationPhase(string current, string next) =>
        string.Equals(current, next, StringComparison.Ordinal) || (current, next) switch
        {
            ("Planned", "Preparing" or "ProvisioningTarget" or "Failed") => true,
            ("Preparing", "ProvisioningTarget" or "Validating" or "Cutover" or "Failed") => true,
            ("ProvisioningTarget", "Validating" or "Cutover" or "Failed") => true,
            ("Validating", "Cutover" or "Failed") => true,
            ("Cutover", "RetainingSource" or "RetiringSource" or "RolledBack" or "Failed") => true,
            ("RetainingSource", "Released" or "RolledBack" or "Failed") => true,
            ("RetiringSource", "Released" or "RolledBack" or "Failed") => true,
            _ => false
        };

    private static string? OptionalSafeCode(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireSafeCode(value, name);

    private static string? OptionalCanonicalHash(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException($"{name} must be a canonical SHA-256 hash.");
        return value.ToLowerInvariant();
    }

    private static string RequireCanonicalHash(string? value, string name) =>
        OptionalCanonicalHash(value, name) ?? throw new InvalidOperationException($"{name} must be a canonical SHA-256 hash.");

    private static string RequireSafeReference(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} must be a safe reference.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength || normalized.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ':' or '/' or '+')))
            throw new InvalidOperationException($"{name} must be a safe reference.");
        return normalized;
    }

    private static string? OptionalSafeReference(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireSafeReference(value, name, maxLength);

    private static string RequireSafeToken(string? value, string name, int maxLength)
    {
        var normalized = RequireSafeReference(value, name, maxLength);
        if (normalized.Contains('/', StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal) || normalized.Contains('+', StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} must be a safe token.");
        return normalized;
    }

    private static string? OptionalSafeToken(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireSafeToken(value, name, maxLength);

    private static string RequireCatalogValue(string? value, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128 ||
            !char.IsAsciiLetterOrDigit(normalized[0]) ||
            normalized.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '+')))
            throw new InvalidOperationException($"{name} must be a bounded safe catalog value.");
        return normalized.ToLowerInvariant();
    }

    private static string? OptionalCatalogValue(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireCatalogValue(value, name);

    private static string RequireSlug(string? value)
    {
        var normalized = RequireSafeToken(value, nameof(Models.ElsaInstanceEntity.Slug), 128).ToLowerInvariant();
        if (normalized.Length > 96)
            throw new InvalidOperationException("Slug exceeds the safe bound.");
        return normalized;
    }

    private static void RequireDisplayName(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new InvalidOperationException($"{name} must be a bounded display value.");
    }

    private static string? OptionalSha256Digest(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length != 71 || !normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            normalized[7..].Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException($"{name} must be a SHA-256 digest.");
        return "sha256:" + normalized[7..].ToLowerInvariant();
    }

    private static string? OptionalPlanUri(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var uri = ParseSafeUri(value, name, allowLocalHttp: false);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !IsResolvedPlanPath(uri.AbsolutePath))
            throw new InvalidOperationException($"{name} must be an absolute HTTPS resolved-plan URI.");
        return uri.AbsoluteUri;
    }

    private static bool IsResolvedPlanPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 7
            && path.EndsWith('/' + segments[6], StringComparison.Ordinal)
            && string.Equals(segments[0], "api", StringComparison.Ordinal)
            && string.Equals(segments[1], "workspaces", StringComparison.Ordinal)
            && Guid.TryParseExact(segments[2], "D", out _)
            && string.Equals(segments[3], "instances", StringComparison.Ordinal)
            && Guid.TryParseExact(segments[4], "D", out _)
            && string.Equals(segments[5], "resolved-plans", StringComparison.Ordinal)
            && IsSafeJsonName(segments[6]);
    }

    private static void RequireInstancePlanUri(string value, string name, Guid? workspaceId, Guid instanceId, string planId)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{name} must identify the instance resolved plan.");
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!IsResolvedPlanPath(uri.AbsolutePath) || !Guid.TryParseExact(segments[4], "D", out var uriInstanceId) ||
            uriInstanceId != instanceId || !string.Equals(segments[6], planId, StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} must identify the instance resolved plan.");
        if (workspaceId is not null && (!Guid.TryParseExact(segments[2], "D", out var uriWorkspaceId) || uriWorkspaceId != workspaceId))
            throw new InvalidOperationException($"{name} must identify the instance workspace resolved plan.");
    }

    private static string? OptionalEndpointUri(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var uri = ParseSafeUri(value, name, allowLocalHttp: true);
        var localHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                         System.Net.IPAddress.TryParse(uri.Host, out var address) && System.Net.IPAddress.IsLoopback(address));
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !localHttp)
            throw new InvalidOperationException($"{name} must be a safe HTTPS endpoint URI.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static Uri ParseSafeUri(string value, string name, bool allowLocalHttp)
    {
        var normalized = value.Trim();
        if (normalized.Length > 2048 || normalized.Any(char.IsControl) || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException($"{name} must be a safe absolute URI.");
        if (uri.Host.Contains('*', StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} must not use wildcard hosts.");
        if (!allowLocalHttp && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{name} must be HTTPS.");
        if (HasUnsafeUriPath(uri.AbsolutePath))
            throw new InvalidOperationException($"{name} contains an unsafe path.");
        return uri;
    }

    private static bool HasUnsafeUriPath(string path) =>
        path.Contains('%', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal) ||
        path.Contains("//", StringComparison.Ordinal) || path.Split('/').Any(segment => segment is "." or "..");

    private static string? OptionalAudience(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 256 || !normalized.StartsWith("urn:elsa:", StringComparison.Ordinal) ||
            normalized.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is ':' or '.' or '_' or '-')))
            throw new InvalidOperationException($"{name} must be a safe Elsa audience.");
        return normalized;
    }

    private static string RequireExactAudience(string? value, string expected)
    {
        var normalized = OptionalAudience(value, nameof(Models.ElsaInstanceIdentityBindingEntity.Audience));
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Identity audience must be derived from the instance ID.");
        return normalized!;
    }

    private static string RequireCallbackUri(string? value, string verifiedOrigin)
    {
        var normalized = OptionalEndpointUri(value, nameof(Models.ElsaInstanceIdentityBindingEntity.CanonicalCallbackUri));
        var expected = verifiedOrigin.TrimEnd('/') + "/managed-elsa/handoff/callback";
        if (normalized is null || !string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Canonical callback URI must match the verified endpoint origin.");
        return normalized;
    }

    private static string RequireVerifiedOrigin(string? value)
    {
        var normalized = OptionalEndpointUri(value, nameof(Models.ElsaInstanceIdentityBindingEntity.VerifiedEndpointOrigin));
        if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            ((!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
              !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 System.Net.IPAddress.TryParse(uri.Host, out var address) && System.Net.IPAddress.IsLoopback(address))))) ||
            uri.AbsolutePath is not ("" or "/"))
            throw new InvalidOperationException("A verified endpoint origin must be HTTPS and have no path.");
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static bool BelongsToReleaseLine(string releaseLine, string version) =>
        string.Equals(releaseLine, version, StringComparison.OrdinalIgnoreCase) ||
        version.StartsWith(releaseLine + ".", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSubjectFingerprint(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return OptionalSha256Digest(normalized, nameof(Models.ElsaInstanceAuditEventEntity.OperatorSubject))!;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return "sha256:" + digest;
    }

    private static void ValidateFeatureOverrides(string? json)
    {
        if (json is null || json.Length > 32_768)
            throw new InvalidOperationException("Feature overrides JSON exceeds the safe bound.");
        try
        {
            using var document = JsonDocument.Parse(json, SafeJsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Feature overrides must be an object.");
            if (document.RootElement.EnumerateObject().Count() > 256)
                throw new InvalidOperationException("Feature overrides contain too many entries.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name) || !IsSafeJsonName(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Feature overrides contain an unsafe entry.");
                var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string? kind = null;
                string? value = null;
                foreach (var field in property.Value.EnumerateObject())
                {
                    if (!fields.Add(field.Name) || field.Name is not ("kind" or "value") || field.Value.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException("Feature overrides contain an unsafe entry.");
                    if (field.Name == "kind") kind = field.Value.GetString(); else value = field.Value.GetString();
                }
                if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl) || ContainsSensitiveMarker(value))
                    throw new InvalidOperationException("Feature overrides contain an unsafe value.");
                var normalizedKind = RequireSafeCode(kind, "feature override kind");
                if (!Enum.TryParse<ElsaFeatureOverrideKind>(normalizedKind, ignoreCase: true, out var parsedKind) ||
                    !Enum.IsDefined(parsedKind) ||
                    !string.Equals(normalizedKind, parsedKind.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Feature override kind is not supported.");
                switch (parsedKind)
                {
                    case ElsaFeatureOverrideKind.Boolean when !bool.TryParse(value, out _):
                        throw new InvalidOperationException("Boolean feature override values must be true or false.");
                    case ElsaFeatureOverrideKind.Number when !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                        throw new InvalidOperationException("Number feature override values must be invariant decimals.");
                    case ElsaFeatureOverrideKind.Catalog:
                        RequireCatalogValue(value, "feature override value");
                        break;
                }
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Feature overrides JSON is invalid.");
        }
    }

    private static void ValidateComponentDigests(string? json)
    {
        if (json is null)
            return;
        if (json.Length > 65_536)
            throw new InvalidOperationException("Component digest JSON exceeds the safe bound.");
        try
        {
            using var document = JsonDocument.Parse(json, SafeJsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Component digests must be an array.");
            if (document.RootElement.GetArrayLength() is 0 or > 256)
                throw new InvalidOperationException("Component digest count is outside the safe bound.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Component digests contain an unsafe entry.");
                string? componentId = null;
                string? digest = null;
                var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in item.EnumerateObject())
                {
                    if (!fields.Add(field.Name) || field.Name is not ("componentId" or "digest") || field.Value.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException("Component digests contain an unsafe entry.");
                    if (field.Name == "componentId") componentId = field.Value.GetString(); else digest = field.Value.GetString();
                }
                componentId = RequireSafeToken(componentId, "component digest componentId", 128);
                if (!ids.Add(componentId))
                    throw new InvalidOperationException("Component digest IDs must be unique.");
                OptionalSha256Digest(digest, "component digest");
                if (digest is null)
                    throw new InvalidOperationException("Component digest is required.");
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Component digest JSON is invalid.");
        }
    }

    private static readonly string[] ImmutableMigrationProperties =
    [
        nameof(Models.ElsaInstanceMigrationEntity.InstanceId),
        nameof(Models.ElsaInstanceMigrationEntity.OrganizationId),
        nameof(Models.ElsaInstanceMigrationEntity.WorkspaceId),
        nameof(Models.ElsaInstanceMigrationEntity.SourcePlanId),
        nameof(Models.ElsaInstanceMigrationEntity.SourcePlanUri),
        nameof(Models.ElsaInstanceMigrationEntity.SourceReleaseLine),
        nameof(Models.ElsaInstanceMigrationEntity.SourceVersion),
        nameof(Models.ElsaInstanceMigrationEntity.SourceManifestDigest),
        nameof(Models.ElsaInstanceMigrationEntity.SourceDeploymentId),
        nameof(Models.ElsaInstanceMigrationEntity.TargetPlanId),
        nameof(Models.ElsaInstanceMigrationEntity.TargetPlanUri),
        nameof(Models.ElsaInstanceMigrationEntity.TargetReleaseLine),
        nameof(Models.ElsaInstanceMigrationEntity.TargetVersion),
        nameof(Models.ElsaInstanceMigrationEntity.TargetManifestDigest),
        nameof(Models.ElsaInstanceMigrationEntity.TargetDeploymentId),
        nameof(Models.ElsaInstanceMigrationEntity.CreatedAt)
    ];

    private static readonly JsonDocumentOptions SafeJsonOptions = new()
    {
        MaxDepth = 16,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    private static bool IsSafeJsonName(string name) =>
        name.Length is > 0 and <= 128 && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');

    private static bool ContainsSensitiveMarker(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("workflow", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMigrationTuple(Models.ElsaInstanceMigrationEntity migration, bool source)
    {
        var values = source
            ? new string?[] { migration.SourcePlanId, migration.SourcePlanUri, migration.SourceReleaseLine, migration.SourceVersion, migration.SourceManifestDigest, migration.SourceDeploymentId }
            : new string?[] { migration.TargetPlanId, migration.TargetPlanUri, migration.TargetReleaseLine, migration.TargetVersion, migration.TargetManifestDigest, migration.TargetDeploymentId };
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Migration source and target references must be complete tuples.");
        var releaseLine = (source ? migration.SourceReleaseLine : migration.TargetReleaseLine)!.Trim();
        var version = (source ? migration.SourceVersion : migration.TargetVersion)!.Trim();
        if (!BelongsToReleaseLine(releaseLine, version))
            throw new InvalidOperationException("Migration release version must belong to its release line.");
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
