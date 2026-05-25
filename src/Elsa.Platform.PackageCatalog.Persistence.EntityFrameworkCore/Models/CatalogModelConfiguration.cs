using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;
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
        builder.Property(x => x.DeploymentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DriftStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Name }).IsUnique();
        builder.HasMany(x => x.Engines).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Revisions).WithOne(x => x.Environment).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
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
        builder.Property(x => x.HostingProvider).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Name }).IsUnique();
        builder.HasMany(x => x.Capabilities).WithOne(x => x.Engine).HasForeignKey(x => x.EngineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Controls).WithOne(x => x.Engine).HasForeignKey(x => x.EngineId).OnDelete(DeleteBehavior.Cascade);
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
    }
}
