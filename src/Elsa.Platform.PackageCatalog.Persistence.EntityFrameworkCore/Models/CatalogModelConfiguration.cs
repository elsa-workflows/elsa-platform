using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Manifests;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal sealed class PackageSourceConfiguration : IEntityTypeConfiguration<PackageSource>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right)),
        value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        value => value == null ? new List<string>() : value.ToList());

    public void Configure(EntityTypeBuilder<PackageSource> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.LastSyncError).HasMaxLength(2048);
        builder.Property(x => x.PollingInterval).HasMaxLength(64);
        builder.Property(x => x.Browseable).HasDefaultValue(true);
        builder.Property(x => x.Visibility).HasDefaultValue(PackageSourceVisibility.Public);
        builder.Property(x => x.VersionDiscoveryPolicy).HasDefaultValue(PackageSourceVersionDiscoveryPolicy.AllVersions);
        builder.Property(x => x.IncludePatterns)
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(StringListComparer);
        builder.Property(x => x.ExcludePatterns)
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(StringListComparer);
        builder.HasMany(x => x.Packages).WithOne(x => x.Source).HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.OwnerWorkspace).WithMany().HasForeignKey(x => x.OwnerWorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PackageId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.SourceId, x.PackageId }).IsUnique();
        builder.HasMany(x => x.Versions).WithOne(x => x.Package).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackageVersionConfiguration : IEntityTypeConfiguration<PackageVersion>
{
    public void Configure(EntityTypeBuilder<PackageVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ManifestJson).IsRequired();
        builder.Property(x => x.ManifestHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.PackageId, x.Version }).IsUnique();
        builder.HasMany(x => x.Features).WithOne(x => x.PackageVersion).HasForeignKey(x => x.PackageVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FeatureRecordConfiguration : IEntityTypeConfiguration<FeatureRecord>
{
    public void Configure(EntityTypeBuilder<FeatureRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TypeName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.PackageVersionId, x.FeatureId }).IsUnique();
        builder.HasMany(x => x.Settings).WithOne(x => x.FeatureRecord).HasForeignKey(x => x.FeatureRecordId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FeatureSettingRecordConfiguration : IEntityTypeConfiguration<FeatureSettingRecord>
{
    public void Configure(EntityTypeBuilder<FeatureSettingRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.JsonType).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.FeatureRecordId, x.Name }).IsUnique();
    }
}

internal sealed class ManifestValidationResultRecordConfiguration : IEntityTypeConfiguration<ManifestValidationResultRecord>
{
    public void Configure(EntityTypeBuilder<ManifestValidationResultRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.PackageVersion).WithMany().HasForeignKey(x => x.PackageVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApprovalRecordConfiguration : IEntityTypeConfiguration<ApprovalRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Actor).HasMaxLength(256).IsRequired();
    }
}

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CompletedAt)
            .HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasMany(x => x.Items).WithOne(x => x.SyncRun).HasForeignKey(x => x.SyncRunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SyncRunItemConfiguration : IEntityTypeConfiguration<SyncRunItem>
{
    public void Configure(EntityTypeBuilder<SyncRunItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.PackageVersion).WithMany().HasForeignKey(x => x.PackageVersionId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(256);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.HasMany(x => x.ExternalIdentities).WithOne(x => x.Account).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Memberships).WithOne(x => x.Account).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Issuer).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
    }
}

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasMany(x => x.Memberships).WithOne(x => x.Workspace).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.EntitlementSnapshots).WithOne(x => x.Workspace).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.WorkspaceId, x.AccountId }).IsUnique();
    }
}

internal sealed class WorkspaceEntitlementSnapshotConfiguration : IEntityTypeConfiguration<WorkspaceEntitlementSnapshot>
{
    public void Configure(EntityTypeBuilder<WorkspaceEntitlementSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SyncedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CreatedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => x.WorkspaceId).IsUnique();
    }
}

internal sealed class RuntimeConfigurationConfiguration : IEntityTypeConfiguration<RuntimeConfiguration>
{
    public void Configure(EntityTypeBuilder<RuntimeConfiguration> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.IntentJson).IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.SoftDeletedAt)
            .HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.SoftDeletedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Versions).WithOne(x => x.RuntimeConfiguration).HasForeignKey(x => x.RuntimeConfigurationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RuntimeConfigurationVersionConfiguration : IEntityTypeConfiguration<RuntimeConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<RuntimeConfigurationVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.IntentJson).IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.RuntimeConfigurationId, x.VersionNumber }).IsUnique();
    }
}

internal sealed class DeploymentApplicationConfiguration : IEntityTypeConfiguration<DeploymentApplicationEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentApplicationEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Environments).WithOne(x => x.Application).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentEnvironmentConfiguration : IEntityTypeConfiguration<DeploymentEnvironmentEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentEnvironmentEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TierRequiresReview).HasDefaultValue(false);
        builder.Property(x => x.DeploymentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DriftStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.TierId });
        builder.HasOne(x => x.TierDefinition).WithMany(x => x.Environments).HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Engines).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Revisions).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ObservabilityBindings).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.DriftReports).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentTierDefinitionConfiguration : IEntityTypeConfiguration<DeploymentTierDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentTierDefinitionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.ArchivedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.Status, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.SortOrder });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Capabilities).WithOne(x => x.Tier).HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Changes).WithOne(x => x.Tier).HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentTierCapabilityAssignmentConfiguration : IEntityTypeConfiguration<DeploymentTierCapabilityAssignmentEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentTierCapabilityAssignmentEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CapabilityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.TierId, x.CapabilityId }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.CapabilityId });
    }
}

internal sealed class DeploymentTierChangeRecordConfiguration : IEntityTypeConfiguration<DeploymentTierChangeRecordEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentTierChangeRecordEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChangeType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ChangedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.TierId, x.ChangedAt });
    }
}

internal sealed class WorkflowEngineConfiguration : IEntityTypeConfiguration<WorkflowEngineEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowEngineEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Region).HasMaxLength(128);
        builder.Property(x => x.Version).HasMaxLength(128);
        builder.Property(x => x.CertificateStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CredentialProvider).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CredentialReference).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.CredentialVerificationStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Health).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.VerificationMessage).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.HostingProvider).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Name }).IsUnique();
        builder.HasMany(x => x.Capabilities).WithOne(x => x.Engine).HasForeignKey(x => x.EngineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Controls).WithOne(x => x.Engine).HasForeignKey(x => x.EngineId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkspaceDeploymentArtifactConfiguration : IEntityTypeConfiguration<WorkspaceDeploymentArtifactEntity>
{
    public void Configure(EntityTypeBuilder<WorkspaceDeploymentArtifactEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ArtifactId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.LayoutVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ContentDigestAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentDigest).HasMaxLength(256).IsRequired();
        builder.Property(x => x.EnvelopeVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ArtifactTypeId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ArtifactSchemaVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ManifestDigestAlgorithm).HasMaxLength(32);
        builder.Property(x => x.ManifestDigest).HasMaxLength(256);
        builder.Property(x => x.PayloadReferenceJson).IsRequired();
        builder.Property(x => x.ProducerJson).IsRequired();
        builder.Property(x => x.DisplayMetadataJson).IsRequired();
        builder.Property(x => x.CompatibilityHintsJson).IsRequired();
        builder.Property(x => x.Format).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ReferenceProvider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ManifestName).HasMaxLength(256);
        builder.Property(x => x.ManifestVersion).HasMaxLength(128);
        builder.Property(x => x.ManifestEnvironment).HasMaxLength(128);
        builder.Property(x => x.ResourceSummaryJson).IsRequired();
        builder.Property(x => x.ChecksumStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.InspectionStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DiagnosticsJson).IsRequired();
        builder.Property(x => x.RegisteredAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.ArtifactId }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.RegisteredAt });
    }
}

internal sealed class EngineCapabilityConfiguration : IEntityTypeConfiguration<EngineCapabilityEntity>
{
    public void Configure(EntityTypeBuilder<EngineCapabilityEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CapabilityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Boundary).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.EngineId, x.CapabilityId }).IsUnique();
    }
}

internal sealed class RuntimeControlConfiguration : IEntityTypeConfiguration<RuntimeControlEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeControlEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ControlId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Boundary).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RequiredCapabilityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.EngineId, x.ControlId }).IsUnique();
    }
}

internal sealed class RuntimeControlExecutionConfiguration : IEntityTypeConfiguration<RuntimeControlExecutionEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeControlExecutionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ControlId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ControlLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Boundary).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RequiredCapabilityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.EngineId, x.ControlId, x.CreatedAt });
        builder.HasOne(x => x.Engine).WithMany().HasForeignKey(x => x.EngineId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DesiredStateRevisionConfiguration : IEntityTypeConfiguration<DesiredStateRevisionEntity>
{
    public void Configure(EntityTypeBuilder<DesiredStateRevisionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Commit).HasMaxLength(128);
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DesiredStateJson).IsRequired();
        builder.Property(x => x.AuthoredAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.EnvironmentId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.ContentHash });
        builder.HasMany(x => x.Records).WithOne(x => x.Revision).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StructuredDesiredStateRecordConfiguration : IEntityTypeConfiguration<StructuredDesiredStateRecordEntity>
{
    public void Configure(EntityTypeBuilder<StructuredDesiredStateRecordEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.RevisionId, x.Kind, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.ContentHash });
    }
}

internal sealed class WorkspacePermissionGrantConfiguration : IEntityTypeConfiguration<WorkspacePermissionGrantEntity>
{
    public void Configure(EntityTypeBuilder<WorkspacePermissionGrantEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Permission).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.RevokedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.AccountId, x.Permission, x.RevokedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ActionConfirmationConfiguration : IEntityTypeConfiguration<ActionConfirmationEntity>
{
    public void Configure(EntityTypeBuilder<ActionConfirmationEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.TargetId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConfirmedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.ExpiresAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UsedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.ActionType, x.TargetId, x.ConfirmedByAccountId });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentRunConfiguration : IEntityTypeConfiguration<DeploymentRunEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentRunEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.ValidationOutcome).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.QueuedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.StartedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.CompletedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.WorkerId).HasMaxLength(200);
        builder.Property(x => x.WorkerHeartbeatAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.RecoveryReason).HasMaxLength(1000);
        builder.Property(x => x.FailureMessage).HasMaxLength(2000);
        builder.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Status });
        builder.HasOne(x => x.Environment).WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.History).WithOne(x => x.Run).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentRunHistoryEventConfiguration : IEntityTypeConfiguration<DeploymentRunHistoryEventEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentRunHistoryEventEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.RunId, x.CreatedAt });
    }
}

internal sealed class DeploymentCommandConfiguration : IEntityTypeConfiguration<DeploymentCommandEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentCommandEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.ArtifactJson).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.WorkerId).HasMaxLength(256);
        builder.Property(x => x.LeaseToken).HasMaxLength(256);
        builder.Property(x => x.ProgressMessage).HasMaxLength(1000);
        builder.Property(x => x.ObservedArtifactDigestAlgorithm).HasMaxLength(32);
        builder.Property(x => x.ObservedArtifactDigest).HasMaxLength(256);
        builder.Property(x => x.RuntimeReference).HasMaxLength(1024);
        builder.Property(x => x.DiagnosticsJson).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.AvailableAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.ExpiresAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.ClaimedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.LeaseExpiresAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.HeartbeatAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.Property(x => x.CompletedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.EngineId, x.Status, x.AvailableAt });
        builder.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey }).IsUnique();
        builder.HasMany(x => x.Events).WithOne(x => x.Command).HasForeignKey(x => x.CommandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeploymentCommandEventConfiguration : IEntityTypeConfiguration<DeploymentCommandEventEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentCommandEventEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.CommandId, x.CreatedAt });
        builder.HasIndex(x => new { x.WorkspaceId, x.RunId, x.CreatedAt });
    }
}

internal sealed class DeploymentCommandWebhookNotificationConfiguration : IEntityTypeConfiguration<DeploymentCommandWebhookNotificationEntity>
{
    public void Configure(EntityTypeBuilder<DeploymentCommandWebhookNotificationEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.SafePayloadJson).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.SentAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.EngineId, x.CreatedAt });
        builder.HasIndex(x => x.CommandId);
    }
}

internal sealed class ObservabilityBindingConfiguration : IEntityTypeConfiguration<ObservabilityBindingEntity>
{
    public void Configure(EntityTypeBuilder<ObservabilityBindingEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Provider).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Scope).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Sample).HasMaxLength(2000);
        builder.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Kind });
    }
}

internal sealed class DriftReportItemConfiguration : IEntityTypeConfiguration<DriftReportItemEntity>
{
    public void Configure(EntityTypeBuilder<DriftReportItemEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Area).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Desired).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Observed).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.DetectedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.EngineId, x.DetectedAt });
    }
}
