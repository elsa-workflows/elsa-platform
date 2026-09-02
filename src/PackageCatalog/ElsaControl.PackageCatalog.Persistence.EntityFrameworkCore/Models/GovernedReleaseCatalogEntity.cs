using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal interface IGovernedReleaseCatalogEntity;

internal sealed class GovernedReleaseCatalogEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public string CatalogIdentityHash { get; set; } = "";
    public string ProjectionFingerprint { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string ManifestReference { get; set; } = "";
    public string ManifestDigest { get; set; } = "";
    public string PayloadDigest { get; set; } = "";
    public string SignatureEvidenceReference { get; set; } = "";
    public string SignatureEvidenceDigest { get; set; } = "";
    public string RegistryClass { get; set; } = "";
    public string DistributionId { get; set; } = "";
    public string Generation { get; set; } = "";
    public string ReleaseLine { get; set; } = "";
    public string ReleaseVersion { get; set; } = "";
    public string Channel { get; set; } = "";
    public string ProducerLifecycle { get; set; } = "";
    public string? Edition { get; set; }
    public string SourceRepository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string SourceRunId { get; set; } = "";
    // Control owns this value. It is initialized from server-side policy and remains
    // separate from the producer assertion above.
    public string CatalogLifecycle { get; set; } = "";
    public long AdmittedAtUtcTicks { get; set; }
    public string? ComponentDeclarationsFormat { get; set; }
    public string? ComponentDeclarationsDigest { get; set; }

    public ICollection<GovernedReleaseCatalogTopologyEntity> Topologies { get; } = [];
    public ICollection<GovernedReleaseCatalogPackageDeclarationEntity> PackageDeclarations { get; } = [];
}

internal sealed class GovernedReleaseCatalogPackageDeclarationEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public GovernedReleaseCatalogEntity Release { get; set; } = null!;
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogTopologyEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public GovernedReleaseCatalogEntity Release { get; set; } = null!;
    public string TopologyId { get; set; } = "";
    public string PackageManifestSchema { get; set; } = "";
    public ICollection<GovernedReleaseCatalogRuntimeKindEntity> RuntimeKinds { get; } = [];
    public ICollection<GovernedReleaseCatalogCapabilityEntity> Capabilities { get; } = [];
    public ICollection<GovernedReleaseCatalogComponentVersionEntity> ComponentVersions { get; } = [];
    public ICollection<GovernedReleaseCatalogComponentEntity> Components { get; } = [];
    public ICollection<GovernedReleaseCatalogEvidenceEntity> Evidence { get; } = [];
}

internal sealed class GovernedReleaseCatalogRuntimeKindEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid TopologyId { get; set; }
    public GovernedReleaseCatalogTopologyEntity Topology { get; set; } = null!;
    public string RuntimeKind { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogCapabilityEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid TopologyId { get; set; }
    public GovernedReleaseCatalogTopologyEntity Topology { get; set; } = null!;
    public string Capability { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogComponentVersionEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid TopologyId { get; set; }
    public GovernedReleaseCatalogTopologyEntity Topology { get; set; } = null!;
    public string ComponentId { get; set; } = "";
    public string Version { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogComponentEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid TopologyId { get; set; }
    public GovernedReleaseCatalogTopologyEntity Topology { get; set; } = null!;
    public string ComponentId { get; set; } = "";
    public string ImageReference { get; set; } = "";
    public string ImageDigest { get; set; } = "";
    public string? CompanionComponentId { get; set; }
    public ICollection<GovernedReleaseCatalogPlatformDigestEntity> PlatformDigests { get; } = [];
    public ICollection<GovernedReleaseCatalogRoleEntity> Roles { get; } = [];
    public ICollection<GovernedReleaseCatalogComponentCapabilityEntity> Capabilities { get; } = [];
    public ICollection<GovernedReleaseCatalogEndpointEntity> Endpoints { get; } = [];
}

internal sealed class GovernedReleaseCatalogPlatformDigestEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public GovernedReleaseCatalogComponentEntity Component { get; set; } = null!;
    public string Platform { get; set; } = "";
    public string Digest { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogRoleEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public GovernedReleaseCatalogComponentEntity Component { get; set; } = null!;
    public string Role { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogComponentCapabilityEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public GovernedReleaseCatalogComponentEntity Component { get; set; } = null!;
    public string Capability { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogEndpointEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public GovernedReleaseCatalogComponentEntity Component { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "";
    public int Port { get; set; }
    public string Visibility { get; set; } = "";
    public bool RequiresTls { get; set; }
    public string? Path { get; set; }
}

internal sealed class GovernedReleaseCatalogEvidenceEntity : IGovernedReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public Guid TopologyId { get; set; }
    public GovernedReleaseCatalogTopologyEntity Topology { get; set; } = null!;
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Digest { get; set; } = "";
}

internal sealed class GovernedReleaseCatalogConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CatalogIdentityHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProjectionFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(GovernedReleaseCatalogFieldLimits.SchemaVersion).IsRequired();
        builder.Property(x => x.ManifestReference).HasMaxLength(GovernedReleaseCatalogFieldLimits.ManifestReference).IsRequired();
        builder.Property(x => x.ManifestDigest).HasMaxLength(GovernedReleaseCatalogFieldLimits.ManifestDigest).IsRequired();
        builder.Property(x => x.PayloadDigest).HasMaxLength(GovernedReleaseCatalogFieldLimits.PayloadDigest).IsRequired();
        builder.Property(x => x.SignatureEvidenceReference).HasMaxLength(GovernedReleaseCatalogFieldLimits.SignatureEvidenceReference).IsRequired();
        builder.Property(x => x.SignatureEvidenceDigest).HasMaxLength(GovernedReleaseCatalogFieldLimits.SignatureEvidenceDigest).IsRequired();
        builder.Property(x => x.RegistryClass).HasMaxLength(GovernedReleaseCatalogFieldLimits.RegistryClass).IsRequired();
        builder.Property(x => x.DistributionId).HasMaxLength(GovernedReleaseCatalogFieldLimits.DistributionId).IsRequired();
        builder.Property(x => x.Generation).HasMaxLength(GovernedReleaseCatalogFieldLimits.Generation).IsRequired();
        builder.Property(x => x.ReleaseLine).HasMaxLength(GovernedReleaseCatalogFieldLimits.ReleaseLine).IsRequired();
        builder.Property(x => x.ReleaseVersion).HasMaxLength(GovernedReleaseCatalogFieldLimits.ReleaseVersion).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(GovernedReleaseCatalogFieldLimits.Channel).IsRequired();
        builder.Property(x => x.ProducerLifecycle).HasMaxLength(GovernedReleaseCatalogFieldLimits.ProducerLifecycle).IsRequired();
        builder.Property(x => x.Edition).HasMaxLength(GovernedReleaseCatalogFieldLimits.Edition);
        builder.Property(x => x.SourceRepository).HasMaxLength(GovernedReleaseCatalogFieldLimits.SourceRepository).IsRequired();
        builder.Property(x => x.SourceCommit).HasMaxLength(GovernedReleaseCatalogFieldLimits.SourceCommit).IsRequired();
        builder.Property(x => x.SourceRunId).HasMaxLength(GovernedReleaseCatalogFieldLimits.SourceRunId).IsRequired();
        builder.Property(x => x.CatalogLifecycle).HasMaxLength(GovernedReleaseCatalogFieldLimits.CatalogLifecycle).IsRequired();
        builder.Property(x => x.ComponentDeclarationsFormat).HasMaxLength(GovernedReleaseCatalogFieldLimits.ComponentDeclarationsFormat);
        builder.Property(x => x.ComponentDeclarationsDigest).HasMaxLength(GovernedReleaseCatalogFieldLimits.ComponentDeclarationsDigest);
        builder.HasIndex(x => x.CatalogIdentityHash).IsUnique();
        builder.HasIndex(x => new { x.ManifestDigest, x.RegistryClass }).IsUnique();
        builder.HasIndex(x => new { x.DistributionId, x.Generation, x.ReleaseLine, x.ReleaseVersion, x.RegistryClass }).IsUnique();
        builder.HasIndex(x => new { x.CatalogLifecycle, x.Channel, x.ProducerLifecycle, x.ReleaseLine, x.ReleaseVersion, x.Id });
        builder.HasMany(x => x.Topologies).WithOne(x => x.Release).HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.PackageDeclarations).WithOne(x => x.Release).HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GovernedReleaseCatalogPackageDeclarationConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogPackageDeclarationEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogPackageDeclarationEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogPackageDeclarations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PackageId).HasMaxLength(GovernedReleaseCatalogFieldLimits.ReleasePackageId).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(GovernedReleaseCatalogFieldLimits.ReleasePackageVersion).IsRequired();
        builder.HasIndex(x => new { x.ReleaseId, x.PackageId }).IsUnique();
    }
}

internal sealed class GovernedReleaseCatalogTopologyConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogTopologyEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogTopologyEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogTopologies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TopologyId).HasMaxLength(GovernedReleaseCatalogFieldLimits.TopologyId).IsRequired();
        builder.Property(x => x.PackageManifestSchema).HasMaxLength(GovernedReleaseCatalogFieldLimits.PackageManifestSchema).IsRequired();
        builder.HasIndex(x => new { x.ReleaseId, x.TopologyId }).IsUnique();
        builder.HasIndex(x => new { x.TopologyId, x.ReleaseId });
        builder.HasMany(x => x.RuntimeKinds).WithOne(x => x.Topology).HasForeignKey(x => x.TopologyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Capabilities).WithOne(x => x.Topology).HasForeignKey(x => x.TopologyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ComponentVersions).WithOne(x => x.Topology).HasForeignKey(x => x.TopologyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Components).WithOne(x => x.Topology).HasForeignKey(x => x.TopologyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Evidence).WithOne(x => x.Topology).HasForeignKey(x => x.TopologyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GovernedReleaseCatalogRuntimeKindConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogRuntimeKindEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogRuntimeKindEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogRuntimeKinds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RuntimeKind).HasMaxLength(GovernedReleaseCatalogFieldLimits.RuntimeKind).IsRequired();
        builder.HasIndex(x => new { x.TopologyId, x.RuntimeKind }).IsUnique();
        builder.HasIndex(x => new { x.RuntimeKind, x.TopologyId });
    }
}

internal sealed class GovernedReleaseCatalogCapabilityConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogCapabilityEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogCapabilityEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogCapabilities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Capability).HasMaxLength(GovernedReleaseCatalogFieldLimits.Capability).IsRequired();
        builder.HasIndex(x => new { x.TopologyId, x.Capability }).IsUnique();
        builder.HasIndex(x => new { x.Capability, x.TopologyId });
    }
}

internal sealed class GovernedReleaseCatalogComponentVersionConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogComponentVersionEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogComponentVersionEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogComponentVersions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ComponentId).HasMaxLength(GovernedReleaseCatalogFieldLimits.ComponentId).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(GovernedReleaseCatalogFieldLimits.ComponentVersion).IsRequired();
        builder.HasIndex(x => new { x.TopologyId, x.ComponentId }).IsUnique();
    }
}

internal sealed class GovernedReleaseCatalogComponentConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogComponentEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogComponentEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogComponents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ComponentId).HasMaxLength(GovernedReleaseCatalogFieldLimits.ComponentId).IsRequired();
        builder.Property(x => x.ImageReference).HasMaxLength(GovernedReleaseCatalogFieldLimits.ImageReference).IsRequired();
        builder.Property(x => x.ImageDigest).HasMaxLength(GovernedReleaseCatalogFieldLimits.ImageDigest).IsRequired();
        builder.Property(x => x.CompanionComponentId).HasMaxLength(GovernedReleaseCatalogFieldLimits.CompanionComponentId);
        builder.HasIndex(x => new { x.TopologyId, x.ComponentId }).IsUnique();
        builder.HasMany(x => x.PlatformDigests).WithOne(x => x.Component).HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Roles).WithOne(x => x.Component).HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Capabilities).WithOne(x => x.Component).HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Endpoints).WithOne(x => x.Component).HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GovernedReleaseCatalogPlatformDigestConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogPlatformDigestEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogPlatformDigestEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogPlatformDigests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(GovernedReleaseCatalogFieldLimits.Platform).IsRequired();
        builder.Property(x => x.Digest).HasMaxLength(GovernedReleaseCatalogFieldLimits.PlatformDigest).IsRequired();
        builder.HasIndex(x => new { x.ComponentId, x.Platform }).IsUnique();
    }
}

internal sealed class GovernedReleaseCatalogRoleConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogRoleEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogRoleEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogRoles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasMaxLength(GovernedReleaseCatalogFieldLimits.Role).IsRequired();
        builder.HasIndex(x => new { x.ComponentId, x.Role }).IsUnique();
    }
}

internal sealed class GovernedReleaseCatalogComponentCapabilityConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogComponentCapabilityEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogComponentCapabilityEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogComponentCapabilities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Capability).HasMaxLength(GovernedReleaseCatalogFieldLimits.Capability).IsRequired();
        builder.HasIndex(x => new { x.ComponentId, x.Capability }).IsUnique();
        builder.HasIndex(x => new { x.Capability, x.ComponentId });
    }
}

internal sealed class GovernedReleaseCatalogEndpointConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogEndpointEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogEndpointEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogEndpoints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(GovernedReleaseCatalogFieldLimits.EndpointName).IsRequired();
        builder.Property(x => x.Protocol).HasMaxLength(GovernedReleaseCatalogFieldLimits.EndpointProtocol).IsRequired();
        builder.Property(x => x.Visibility).HasMaxLength(GovernedReleaseCatalogFieldLimits.EndpointVisibility).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(GovernedReleaseCatalogFieldLimits.EndpointPath);
        builder.HasIndex(x => new { x.ComponentId, x.Name }).IsUnique();
    }
}

internal sealed class GovernedReleaseCatalogEvidenceConfiguration : IEntityTypeConfiguration<GovernedReleaseCatalogEvidenceEntity>
{
    public void Configure(EntityTypeBuilder<GovernedReleaseCatalogEvidenceEntity> builder)
    {
        builder.ToTable("GovernedReleaseCatalogEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(GovernedReleaseCatalogFieldLimits.EvidenceKind).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(GovernedReleaseCatalogFieldLimits.EvidenceReference).IsRequired();
        builder.Property(x => x.Digest).HasMaxLength(GovernedReleaseCatalogFieldLimits.EvidenceDigest).IsRequired();
        builder.HasIndex(x => new { x.TopologyId, x.Kind }).IsUnique();
    }
}
