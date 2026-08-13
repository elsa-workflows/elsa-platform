using ValenceControl.Healing.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ComponentManifestModel = ValenceControl.Healing.Core.ComponentManifest;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore;

public static class HealingModelConfiguration
{
    private const int KeyLength = 256;
    private const int HashLength = 128;
    private const int NameLength = 512;
    private const int SafeDetailLength = 8_192;
    private const int EnvelopeLength = 262_144;
    private const int PatchLength = 1_048_576;

    public static void Configure(ModelBuilder modelBuilder, string? providerName)
    {
        ConfigureConfiguration(modelBuilder.Entity<HealingConfiguration>());
        ConfigureWorkspaceConfiguration(modelBuilder.Entity<HealingWorkspaceConfiguration>());
        ConfigureEnvironmentConfiguration(modelBuilder.Entity<HealingEnvironmentConfiguration>());
        ConfigureTelemetrySource(modelBuilder.Entity<HealingTelemetrySource>());
        ConfigureInbox(modelBuilder.Entity<HealingSignalInboxItem>());
        ConfigureManifest(modelBuilder.Entity<ComponentManifestModel>());
        ConfigureManifestRegistration(modelBuilder.Entity<ComponentManifestRegistration>());
        ConfigureManifestEntry(modelBuilder.Entity<ComponentManifestEntry>());
        ConfigureManifestAssembly(modelBuilder.Entity<ComponentManifestAssemblyArtifact>());
        ConfigureComponentDependency(modelBuilder.Entity<ComponentDependency>());
        ConfigureBinding(modelBuilder.Entity<SourceOwnershipBinding>(), providerName);
        ConfigureOccurrence(modelBuilder.Entity<IncidentOccurrence>());
        ConfigureAttribution(modelBuilder.Entity<ComponentAttribution>());
        ConfigureIncident(modelBuilder.Entity<HealingIncident>(), providerName);
        ConfigureEpisode(modelBuilder.Entity<IncidentEpisode>(), providerName);
        ConfigureEnvironmentImpact(modelBuilder.Entity<EnvironmentImpact>());
        ConfigureWorkItemProjection(modelBuilder.Entity<RepairWorkItemProjection>());
        ConfigureAttempt(modelBuilder.Entity<RepairAttempt>());
        ConfigureManagedRepairProposal(modelBuilder.Entity<ManagedRepairProposal>());
        ConfigureManagedRepairInferenceReservation(modelBuilder.Entity<ManagedRepairInferenceReservation>());
        ConfigureEvidence(modelBuilder.Entity<EvidenceBundle>());
        ConfigureEvidenceDecision(modelBuilder.Entity<EvidenceAccessDecision>());
        ConfigureRepairResult(modelBuilder.Entity<RepairResult>());
        ConfigurePullRequest(modelBuilder.Entity<RepairPullRequest>());
        ConfigurePolicies(modelBuilder);
        ConfigurePolicyEvaluation(modelBuilder.Entity<PolicyEvaluation>());
        ConfigureProviderConnection(modelBuilder.Entity<ProviderConnection>());
        ConfigureProviderOperation(modelBuilder.Entity<ProviderOperation>());
        ConfigureProviderMutationJournal(modelBuilder.Entity<ProviderMutationJournalEntry>());
        ConfigureWorkloadExchange(modelBuilder.Entity<WorkloadIdentityExchange>());
        ConfigureWorkloadHeartbeat(modelBuilder.Entity<WorkloadHeartbeat>());
        ConfigureWebhook(modelBuilder.Entity<ProviderWebhookDelivery>());
        ConfigureHumanCommand(modelBuilder.Entity<HumanCommand>());
        ConfigureProviderActorIdentityLink(modelBuilder.Entity<ProviderActorIdentityLink>());
        ConfigureDeploymentObservation(modelBuilder.Entity<DeploymentObservation>());
        ConfigureVerification(modelBuilder.Entity<VerificationResult>());
        ConfigureRepairVerificationFailureOutbox(modelBuilder.Entity<RepairVerificationFailureOutboxItem>());
        ConfigureAudit(modelBuilder.Entity<HealingAuditEvent>());
        ConfigureUtcTimestamps(modelBuilder);
    }

    private static void ConfigureConfiguration(EntityTypeBuilder<HealingConfiguration> entity)
    {
        entity.ToTable("HealingConfigurations");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId }).IsUnique();
        entity.Property(x => x.SignalProfileVersion).HasMaxLength(32).IsRequired();
        Required(entity, x => x.ClassificationPolicyJson, SafeDetailLength);
        Concurrency(entity, x => x.Version);
        entity.HasMany(x => x.Environments).WithOne()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.HealingConfigurationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureWorkspaceConfiguration(EntityTypeBuilder<HealingWorkspaceConfiguration> entity)
    {
        entity.ToTable("HealingWorkspaceConfigurations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.WorkspaceId).IsUnique();
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureEnvironmentConfiguration(EntityTypeBuilder<HealingEnvironmentConfiguration> entity)
    {
        entity.ToTable("HealingEnvironmentConfigurations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.EnvironmentId }).IsUnique();
        Required(entity, x => x.ClassificationPolicyJson, SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureTelemetrySource(EntityTypeBuilder<HealingTelemetrySource> entity)
    {
        entity.ToTable("HealingTelemetrySources");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.EnvironmentId, x.Status });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.EnvironmentId, x.Name });
        Required(entity, x => x.Name, KeyLength);
        entity.Property(x => x.CredentialSalt).HasMaxLength(32).IsRequired();
        entity.Property(x => x.CredentialHash).HasMaxLength(32).IsRequired();
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureInbox(EntityTypeBuilder<HealingSignalInboxItem> entity)
    {
        entity.ToTable("HealingSignalInboxItems");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.ProfileVersion, 32);
        Required(entity, x => x.RedactedEnvelopeJson, EnvelopeLength);
        Required(entity, x => x.EnvelopeHash, HashLength);
        entity.Property(x => x.LeaseOwner).HasMaxLength(KeyLength);
        entity.Property(x => x.LeaseToken).HasMaxLength(KeyLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        entity.Property(x => x.SafeOutcomeDetail).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureRepairVerificationFailureOutbox(EntityTypeBuilder<RepairVerificationFailureOutboxItem> entity)
    {
        entity.ToTable("HealingRepairVerificationFailureOutbox");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentOccurrence>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.SupportingOccurrenceId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.PayloadJson, EnvelopeLength);
        Required(entity, x => x.PayloadHash, HashLength);
        entity.Property(x => x.LeaseOwner).HasMaxLength(KeyLength);
        entity.Property(x => x.LeaseToken).HasMaxLength(KeyLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureManifest(EntityTypeBuilder<ComponentManifestModel> entity)
    {
        entity.ToTable("HealingComponentManifests");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.RevisionId }).IsUnique();
        Required(entity, x => x.SchemaVersion, 32);
        Required(entity, x => x.SourceRevision, KeyLength);
        entity.Property(x => x.BuildId).HasMaxLength(KeyLength);
        Required(entity, x => x.ManifestDigest, HashLength);
        Required(entity, x => x.CanonicalJson, EnvelopeLength);
        entity.Property(x => x.VerifiedBy).HasMaxLength(KeyLength);
        entity.Property(x => x.VerificationMethod).HasMaxLength(KeyLength);
        entity.HasMany(x => x.Entries).WithOne()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(x => x.Dependencies).WithOne().HasForeignKey(x => x.ManifestId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureManifestRegistration(EntityTypeBuilder<ComponentManifestRegistration> entity)
    {
        entity.ToTable("HealingComponentManifestRegistrations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.RevisionId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId });
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.PayloadHash, HashLength);
        entity.HasOne<ComponentManifestModel>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureManifestEntry(EntityTypeBuilder<ComponentManifestEntry> entity)
    {
        entity.ToTable("HealingComponentManifestEntries");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.ManifestId, x.ComponentKey }).IsUnique();
        entity.HasAlternateKey(x => new { x.ManifestId, x.Id });
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.Id });
        Required(entity, x => x.ComponentKey, KeyLength);
        Required(entity, x => x.KindName, 64);
        Required(entity, x => x.Name, NameLength);
        entity.Property(x => x.Version).HasMaxLength(KeyLength);
        entity.Property(x => x.PackageId).HasMaxLength(NameLength);
        entity.Property(x => x.PackageVersion).HasMaxLength(KeyLength);
        entity.Property(x => x.AssemblyName).HasMaxLength(NameLength);
        entity.Property(x => x.AssemblyVersion).HasMaxLength(KeyLength);
        entity.Property(x => x.PublicKeyToken).HasMaxLength(KeyLength);
        Required(entity, x => x.ContentHash, HashLength);
        entity.Property(x => x.RelativePath).HasMaxLength(2_048);
        entity.Property(x => x.RepositoryUrl).HasMaxLength(2_048);
        entity.Property(x => x.RepositoryCommit).HasMaxLength(KeyLength);
        entity.Property(x => x.SourceRoot).HasMaxLength(2_048);
        entity.HasMany(x => x.Assemblies).WithOne()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.ComponentEntryId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureManifestAssembly(EntityTypeBuilder<ComponentManifestAssemblyArtifact> entity)
    {
        entity.ToTable("HealingComponentManifestAssemblies");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.ManifestId, x.ComponentEntryId, x.RelativePath }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.ComponentEntryId });
        Required(entity, x => x.Name, NameLength);
        entity.Property(x => x.Version).HasMaxLength(KeyLength);
        entity.Property(x => x.PublicKeyToken).HasMaxLength(KeyLength);
        Required(entity, x => x.RelativePath, 2_048);
        Required(entity, x => x.ContentHash, HashLength);
    }

    private static void ConfigureComponentDependency(EntityTypeBuilder<ComponentDependency> entity)
    {
        entity.ToTable("HealingComponentDependencies");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.ManifestId, x.FromEntryId, x.ToEntryId }).IsUnique();
        entity.HasOne<ComponentManifestEntry>().WithMany()
            .HasForeignKey(x => new { x.ManifestId, x.FromEntryId })
            .HasPrincipalKey(x => new { x.ManifestId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ComponentManifestEntry>().WithMany()
            .HasForeignKey(x => new { x.ManifestId, x.ToEntryId })
            .HasPrincipalKey(x => new { x.ManifestId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureBinding(EntityTypeBuilder<SourceOwnershipBinding> entity, string? providerName)
    {
        entity.ToTable("HealingSourceOwnershipBindings");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        var activeNameIndex = entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Name }).IsUnique();
        activeNameIndex.HasFilter(providerName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? $"[Status] = {(int)SourceOwnershipBindingStatus.Active}"
            : $"\"Status\" = {(int)SourceOwnershipBindingStatus.Active}");
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Status });
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PathPolicy>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.PathPolicyId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<EvidencePolicy>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EvidencePolicyId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<MergePolicy>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.MergePolicyId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.Name, NameLength);
        Required(entity, x => x.SelectorPattern, 2_048);
        Required(entity, x => x.RepositoryProviderId, KeyLength);
        Required(entity, x => x.RepositoryOwner, NameLength);
        Required(entity, x => x.RepositoryName, NameLength);
        Required(entity, x => x.TargetBranch, KeyLength);
        Required(entity, x => x.WorkflowIdentity, 2_048);
        Required(entity, x => x.WorkflowReference, 2_048);
        Required(entity, x => x.WorkflowRevision, KeyLength);
        entity.Property(x => x.ApprovedBy).HasMaxLength(KeyLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureOccurrence(EntityTypeBuilder<IncidentOccurrence> entity)
    {
        entity.ToTable("HealingIncidentOccurrences");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => x.InboxItemId).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.OccurrenceKey }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.FingerprintVersion, x.Fingerprint });
        entity.HasOne<HealingSignalInboxItem>().WithOne()
            .HasForeignKey<IncidentOccurrence>(x => new { x.WorkspaceId, x.ApplicationId, x.InboxItemId })
            .HasPrincipalKey<HealingSignalInboxItem>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.OccurrenceKey, KeyLength);
        Required(entity, x => x.ExceptionType, NameLength);
        Required(entity, x => x.OperationName, NameLength);
        Required(entity, x => x.NormalizedStackJson, EnvelopeLength);
        entity.Property(x => x.TraceId).HasMaxLength(64);
        entity.Property(x => x.SpanId).HasMaxLength(32);
        Required(entity, x => x.FingerprintVersion, 32);
        Required(entity, x => x.Fingerprint, HashLength);
        Required(entity, x => x.EvidenceDigest, HashLength);
    }

    private static void ConfigureAttribution(EntityTypeBuilder<ComponentAttribution> entity)
    {
        entity.ToTable("HealingComponentAttributions");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.OccurrenceId, x.ComponentEntryId }).IsUnique();
        entity.HasOne<IncidentOccurrence>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.OccurrenceId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ComponentManifestEntry>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.ComponentEntryId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SourceOwnershipBinding>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.BindingId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.Property(x => x.Confidence).HasPrecision(5, 4);
        Required(entity, x => x.ReasonCodesJson, SafeDetailLength);
    }

    private static void ConfigureIncident(EntityTypeBuilder<HealingIncident> entity, string? providerName)
    {
        entity.ToTable("HealingIncidents");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        var terminalStatuses = string.Join(", ", new[]
        {
            HealingIncidentStatus.Failed,
            HealingIncidentStatus.Healed,
            HealingIncidentStatus.Superseded,
            HealingIncidentStatus.Waived
        }.Select(x => (int)x));
        var activeIndex = entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.FingerprintVersion, x.Fingerprint, x.RepairRepositoryKey }).IsUnique();
        activeIndex.HasFilter(providerName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? $"[Status] NOT IN ({terminalStatuses})"
            : $"\"Status\" NOT IN ({terminalStatuses})");
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Status, x.LastSeenAt });
        entity.HasIndex(x => new { x.Status, x.ReadyAfter });
        entity.HasOne<SourceOwnershipBinding>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.SelectedBindingId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ComponentManifestEntry>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.SelectedComponentEntryId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithOne()
            .HasForeignKey<HealingIncident>(x => new { x.WorkspaceId, x.ApplicationId, IncidentId = x.Id, EpisodeId = x.ActiveEpisodeId })
            .HasPrincipalKey<IncidentEpisode>(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, EpisodeId = x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RepairWorkItemProjection>().WithOne()
            .HasForeignKey<HealingIncident>(x => new { x.WorkspaceId, x.ApplicationId, IncidentId = x.Id, ProjectionId = x.WorkItemProjectionId })
            .HasPrincipalKey<RepairWorkItemProjection>(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, ProjectionId = x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.FingerprintVersion, 32);
        Required(entity, x => x.Fingerprint, HashLength);
        Required(entity, x => x.RepairRepositoryKey, NameLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureEpisode(EntityTypeBuilder<IncidentEpisode> entity, string? providerName)
    {
        entity.ToTable("HealingIncidentEpisodes");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, EpisodeId = x.Id });
        entity.HasIndex(x => new { x.IncidentId, x.OpenedAt });
        var previousEpisodeIndex = entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.PreviousEpisodeId }).IsUnique();
        previousEpisodeIndex.HasFilter(providerName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? "[PreviousEpisodeId] IS NOT NULL"
            : "\"PreviousEpisodeId\" IS NOT NULL");
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithOne()
            .HasForeignKey<IncidentEpisode>(x => new { x.WorkspaceId, x.ApplicationId, x.PreviousEpisodeId })
            .HasPrincipalKey<IncidentEpisode>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.ProducingRevisionsJson, SafeDetailLength);
        entity.Property(x => x.TargetRevision).HasMaxLength(KeyLength);
        entity.Property(x => x.RegressionReason).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureEnvironmentImpact(EntityTypeBuilder<EnvironmentImpact> entity)
    {
        entity.ToTable("HealingEnvironmentImpacts");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.EpisodeId, x.EnvironmentId }).IsUnique();
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        Required(entity, x => x.ProducingRevisionsJson, SafeDetailLength);
        entity.Property(x => x.CurrentDeployedRevision).HasMaxLength(KeyLength);
        entity.Property(x => x.ClosedByActorId).HasMaxLength(KeyLength);
        Required(entity, x => x.ClassificationPolicyVersion, 32);
        Required(entity, x => x.ClassificationPolicyHash, HashLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureWorkItemProjection(EntityTypeBuilder<RepairWorkItemProjection> entity)
    {
        entity.ToTable("HealingRepairWorkItemProjections");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, ProjectionId = x.Id });
        entity.HasIndex(x => new { x.IncidentId, x.EpisodeId }).IsUnique();
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.Property(x => x.ProviderWorkItemId).HasMaxLength(KeyLength);
        entity.Property(x => x.Url).HasMaxLength(2_048);
        Required(entity, x => x.MachineSummaryHash, HashLength);
        entity.Property(x => x.ProviderState).HasMaxLength(KeyLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureAttempt(EntityTypeBuilder<RepairAttempt> entity)
    {
        entity.ToTable("HealingRepairAttempts");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.EpisodeId, x.TargetRevision, x.AttemptNumber }).IsUnique();
        entity.HasIndex(x => x.NonceHash).IsUnique();
        entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt });
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SourceOwnershipBinding>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.BindingId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<EvidenceBundle>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EvidenceBundleId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.Property(x => x.ProducingRevision).HasMaxLength(KeyLength);
        Required(entity, x => x.TargetRevision, KeyLength);
        Required(entity, x => x.NonceHash, HashLength);
        entity.Property(x => x.LeaseOwner).HasMaxLength(KeyLength);
        entity.Property(x => x.LeaseToken).HasMaxLength(KeyLength);
        Required(entity, x => x.BudgetJson, SafeDetailLength);
        Required(entity, x => x.UsageJson, SafeDetailLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        entity.Property(x => x.SafeOutcomeDetail).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureEvidence(EntityTypeBuilder<EvidenceBundle> entity)
    {
        entity.ToTable("HealingEvidenceBundles");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.Digest });
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.CanonicalJson, EnvelopeLength);
        Required(entity, x => x.Digest, HashLength);
        Required(entity, x => x.ProvenanceJson, SafeDetailLength);
        Required(entity, x => x.OmissionsJson, SafeDetailLength);
    }

    private static void ConfigureManagedRepairProposal(EntityTypeBuilder<ManagedRepairProposal> entity)
    {
        entity.ToTable("HealingManagedRepairProposals");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId }).IsUnique();
        entity.HasIndex(x => x.FinalizationNonceHash).IsUnique();
        entity.HasOne<RepairAttempt>().WithOne()
            .HasForeignKey<ManagedRepairProposal>(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey<RepairAttempt>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.SourceContextDigest, HashLength);
        Required(entity, x => x.ProposalDigest, HashLength);
        Required(entity, x => x.ProposalJson, PatchLength);
        Required(entity, x => x.FinalizationNonceHash, HashLength);
        Required(entity, x => x.ProtectedFinalizationNonce, 2_048);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureManagedRepairInferenceReservation(EntityTypeBuilder<ManagedRepairInferenceReservation> entity)
    {
        entity.ToTable("HealingManagedRepairInferenceReservations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId }).IsUnique();
        entity.HasIndex(x => x.LeaseTokenHash).IsUnique();
        entity.HasOne<RepairAttempt>().WithOne()
            .HasForeignKey<ManagedRepairInferenceReservation>(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey<RepairAttempt>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.SourceContextDigest, HashLength);
        Required(entity, x => x.LeaseTokenHash, HashLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureEvidenceDecision(EntityTypeBuilder<EvidenceAccessDecision> entity)
    {
        entity.ToTable("HealingEvidenceAccessDecisions");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.IncidentId, x.DecidedAt });
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<EvidenceBundle>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.ReleasedBundleId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.RequesterId, KeyLength);
        Required(entity, x => x.RequestedFieldsJson, SafeDetailLength);
        Required(entity, x => x.Purpose, SafeDetailLength);
        Required(entity, x => x.ReasonCodesJson, SafeDetailLength);
        entity.Property(x => x.ApprovedBy).HasMaxLength(KeyLength);
    }

    private static void ConfigureRepairResult(EntityTypeBuilder<RepairResult> entity)
    {
        entity.ToTable("HealingRepairResults");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.AttemptId).IsUnique();
        entity.HasIndex(x => new { x.AttemptId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => x.ProposalId).IsUnique().HasFilter("[ProposalId] IS NOT NULL");
        entity.HasOne<RepairAttempt>().WithOne()
            .HasForeignKey<RepairResult>(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey<RepairAttempt>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.WorkflowRunId, KeyLength);
        Required(entity, x => x.BaseRevision, KeyLength);
        Required(entity, x => x.TargetRevision, KeyLength);
        entity.Property(x => x.Confidence).HasPrecision(5, 4);
        Required(entity, x => x.UnifiedDiff, PatchLength);
        Required(entity, x => x.PatchDigest, HashLength);
        Required(entity, x => x.EnvelopeDigest, HashLength);
        entity.Property(x => x.ProposalDigest).HasMaxLength(HashLength);
        Required(entity, x => x.ChangedPathsJson, SafeDetailLength);
        Required(entity, x => x.ReproductionJson, SafeDetailLength);
        Required(entity, x => x.RegressionJson, SafeDetailLength);
        Required(entity, x => x.ValidationJson, SafeDetailLength);
        Required(entity, x => x.RiskJson, SafeDetailLength);
    }

    private static void ConfigurePullRequest(EntityTypeBuilder<RepairPullRequest> entity)
    {
        entity.ToTable("HealingRepairPullRequests");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.AttemptId).IsUnique();
        entity.HasIndex(x => new { x.ProviderConnectionId, x.ProviderPullRequestId }).IsUnique();
        entity.HasOne<RepairAttempt>().WithOne()
            .HasForeignKey<RepairPullRequest>(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey<RepairAttempt>(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PolicyEvaluation>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.MergePolicyEvaluationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.ProviderPullRequestId, KeyLength);
        Required(entity, x => x.Url, 2_048);
        Required(entity, x => x.Branch, KeyLength);
        Required(entity, x => x.BaseRevision, KeyLength);
        Required(entity, x => x.HeadRevision, KeyLength);
        Required(entity, x => x.PatchDigest, HashLength);
        Required(entity, x => x.CheckSnapshotJson, EnvelopeLength);
        Required(entity, x => x.BranchProtectionSnapshotJson, EnvelopeLength);
        entity.Property(x => x.MergedRevision).HasMaxLength(KeyLength);
        entity.Property(x => x.ClosureReason).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigurePolicies(ModelBuilder modelBuilder)
    {
        var policy = modelBuilder.Entity<HealingPolicyDefinition>();
        policy.ToTable("HealingPolicies");
        policy.HasKey(x => x.Id);
        policy.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        policy.HasDiscriminator<PolicyKind>("PolicyKind")
            .HasValue<PathPolicy>(PolicyKind.Path)
            .HasValue<EvidencePolicy>(PolicyKind.Evidence)
            .HasValue<MergePolicy>(PolicyKind.Merge);
        policy.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Name, x.PolicyVersion }).IsUnique();
        Required(policy, x => x.Name, NameLength);
        Required(policy, x => x.PolicyVersion, 64);
        Required(policy, x => x.PolicyHash, HashLength);
        Concurrency(policy, x => x.Version);
        modelBuilder.Entity<PathPolicy>().Property(x => x.AllowedRootsJson).HasMaxLength(SafeDetailLength).IsRequired();
        modelBuilder.Entity<PathPolicy>().Property(x => x.ForbiddenRootsJson).HasMaxLength(SafeDetailLength).IsRequired();
        modelBuilder.Entity<EvidencePolicy>().Property(x => x.MinimumInferenceConfidence).HasPrecision(5, 4);
        modelBuilder.Entity<EvidencePolicy>().Property(x => x.PermittedFieldsJson).HasMaxLength(SafeDetailLength).IsRequired();
        modelBuilder.Entity<MergePolicy>().Property(x => x.RequiredChecksJson).HasMaxLength(SafeDetailLength).IsRequired();
        modelBuilder.Entity<MergePolicy>().Property(x => x.IndependentVerifier).HasMaxLength(KeyLength);
        modelBuilder.Entity<MergePolicy>().Property(x => x.ForbiddenChangeCategoriesJson).HasMaxLength(SafeDetailLength).IsRequired();
    }

    private static void ConfigurePolicyEvaluation(EntityTypeBuilder<PolicyEvaluation> entity)
    {
        entity.ToTable("HealingPolicyEvaluations");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.AttemptId, x.PolicyId, x.EvaluatedAt });
        entity.HasOne<RepairAttempt>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<HealingPolicyDefinition>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.PolicyId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.PolicyVersion, 64);
        Required(entity, x => x.PolicyHash, HashLength);
        Required(entity, x => x.InputSnapshotHash, HashLength);
        Required(entity, x => x.GateResultsJson, SafeDetailLength);
        Required(entity, x => x.ReasonCodesJson, SafeDetailLength);
    }

    private static void ConfigureProviderConnection(EntityTypeBuilder<ProviderConnection> entity)
    {
        entity.ToTable("HealingProviderConnections");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.Provider, x.RepositoryProviderId });
        Required(entity, x => x.Provider, 64);
        Required(entity, x => x.InstallationId, KeyLength);
        Required(entity, x => x.RepositoryProviderId, KeyLength);
        Required(entity, x => x.RepositoryOwner, NameLength);
        Required(entity, x => x.RepositoryName, NameLength);
        Required(entity, x => x.CredentialReference, 2_048);
        entity.Property(x => x.WebhookSecretReference).HasMaxLength(2_048);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureProviderOperation(EntityTypeBuilder<ProviderOperation> entity)
    {
        entity.ToTable("HealingProviderOperations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ProviderConnectionId, x.Kind, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RepairAttempt>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.PayloadJson, EnvelopeLength);
        Required(entity, x => x.PayloadHash, HashLength);
        entity.Property(x => x.LeaseOwner).HasMaxLength(KeyLength);
        entity.Property(x => x.LeaseToken).HasMaxLength(KeyLength);
        entity.Property(x => x.ProviderCorrelationId).HasMaxLength(KeyLength);
        entity.Property(x => x.ResultJson).HasMaxLength(EnvelopeLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        entity.Property(x => x.SafeError).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureProviderMutationJournal(EntityTypeBuilder<ProviderMutationJournalEntry> entity)
    {
        entity.ToTable("HealingProviderMutationJournal");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ProviderConnectionId, x.Kind, x.IdempotencyKey }).IsUnique();
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.SafePayloadJson, EnvelopeLength);
        Required(entity, x => x.PayloadHash, HashLength);
        entity.Property(x => x.ResultJson).HasMaxLength(EnvelopeLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureWorkloadExchange(EntityTypeBuilder<WorkloadIdentityExchange> entity)
    {
        entity.ToTable("HealingWorkloadIdentityExchanges");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.JwtId).IsUnique();
        entity.HasIndex(x => x.NonceHash).IsUnique();
        entity.HasIndex(x => x.AttemptId);
        entity.HasOne<RepairAttempt>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.Issuer, 2_048);
        Required(entity, x => x.Audience, 2_048);
        Required(entity, x => x.Subject, 2_048);
        Required(entity, x => x.RepositoryProviderId, KeyLength);
        Required(entity, x => x.RepositoryOwner, NameLength);
        Required(entity, x => x.RepositoryName, NameLength);
        Required(entity, x => x.WorkflowReference, 2_048);
        Required(entity, x => x.WorkflowRevision, KeyLength);
        Required(entity, x => x.SourceReference, 2_048);
        Required(entity, x => x.SourceRevision, KeyLength);
        Required(entity, x => x.WorkflowRunId, KeyLength);
        Required(entity, x => x.ActorId, KeyLength);
        Required(entity, x => x.JwtId, KeyLength);
        Required(entity, x => x.NonceHash, HashLength);
        Required(entity, x => x.Phase, 64);
        Required(entity, x => x.ScopesJson, SafeDetailLength);
        entity.Property(x => x.CapabilityTokenHash).HasMaxLength(HashLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureWorkloadHeartbeat(EntityTypeBuilder<WorkloadHeartbeat> entity)
    {
        entity.ToTable("HealingWorkloadHeartbeats");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId, x.IdempotencyKey }).IsUnique();
        entity.HasOne<RepairAttempt>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.IdempotencyKey, KeyLength);
    }

    private static void ConfigureWebhook(EntityTypeBuilder<ProviderWebhookDelivery> entity)
    {
        entity.ToTable("HealingProviderWebhookDeliveries");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ProviderDeliveryId }).IsUnique();
        Required(entity, x => x.ProviderDeliveryId, KeyLength);
        Required(entity, x => x.InstallationId, KeyLength);
        Required(entity, x => x.RepositoryProviderId, KeyLength);
        Required(entity, x => x.Event, KeyLength);
        entity.Property(x => x.Action).HasMaxLength(KeyLength);
        Required(entity, x => x.BodyDigest, HashLength);
        entity.Property(x => x.RetainedBody).HasMaxLength(EnvelopeLength);
        entity.Property(x => x.OutcomeCode).HasMaxLength(KeyLength);
        entity.Property(x => x.SafeOutcomeDetail).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureHumanCommand(EntityTypeBuilder<HumanCommand> entity)
    {
        entity.ToTable("HealingHumanCommands");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.IncidentId, x.RequestedAt });
        entity.HasIndex(x => new { x.WorkspaceId, x.IncidentId, x.IdempotencyKey }).IsUnique();
        entity.HasOne<HealingIncident>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.Command, KeyLength);
        Required(entity, x => x.IdempotencyKey, KeyLength);
        Required(entity, x => x.ProviderActorId, KeyLength);
        Required(entity, x => x.ProviderActorLogin, KeyLength);
        entity.Property(x => x.ControlActorId).HasMaxLength(KeyLength);
        Required(entity, x => x.ProviderPermissionSnapshotJson, SafeDetailLength);
        entity.Property(x => x.ResultCode).HasMaxLength(KeyLength);
        entity.Property(x => x.SafeResultDetail).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureProviderActorIdentityLink(EntityTypeBuilder<ProviderActorIdentityLink> entity)
    {
        entity.ToTable("HealingProviderActorIdentityLinks");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.ProviderConnectionId, x.ProviderActorId }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ControlAccountId });
        entity.HasOne<ProviderConnection>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ProviderConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.ProviderActorId, KeyLength);
        Required(entity, x => x.ProviderActorLogin, KeyLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureDeploymentObservation(EntityTypeBuilder<DeploymentObservation> entity)
    {
        entity.ToTable("HealingDeploymentObservations");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id });
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Source, x.SourceIdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.Source, x.SourceObservationId }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.ApplicationId, x.EnvironmentId, x.Revision });
        Required(entity, x => x.Revision, KeyLength);
        Required(entity, x => x.SourceObservationId, KeyLength);
        Required(entity, x => x.SourceIdempotencyKey, KeyLength);
        Required(entity, x => x.TrustIdentity, 2_048);
        Required(entity, x => x.EvidenceDigest, HashLength);
    }

    private static void ConfigureVerification(EntityTypeBuilder<VerificationResult> entity)
    {
        entity.ToTable("HealingVerificationResults");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.EpisodeId, x.EnvironmentId, x.RepairedRevision }).IsUnique();
        entity.HasOne<IncidentEpisode>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DeploymentObservation>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.DeploymentObservationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentOccurrence>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ApplicationId, x.SupportingOccurrenceId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ApplicationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        Required(entity, x => x.RepairedRevision, KeyLength);
        entity.Property(x => x.SafeDecisionReason).HasMaxLength(SafeDetailLength);
        Concurrency(entity, x => x.Version);
    }

    private static void ConfigureAudit(EntityTypeBuilder<HealingAuditEvent> entity)
    {
        entity.ToTable("HealingAuditEvents");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.WorkspaceId, x.AggregateType, x.AggregateId, x.Sequence }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.AggregateType, x.AggregateId, x.EventType, x.CorrelationId }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.CorrelationId, x.OccurredAt });
        Required(entity, x => x.AggregateType, KeyLength);
        Required(entity, x => x.EventType, KeyLength);
        Required(entity, x => x.ReasonCode, KeyLength);
        Required(entity, x => x.ActorType, 64);
        Required(entity, x => x.ActorId, KeyLength);
        entity.Property(x => x.PolicyVersion).HasMaxLength(64);
        entity.Property(x => x.InputHash).HasMaxLength(HashLength);
        entity.Property(x => x.OutputHash).HasMaxLength(HashLength);
        Required(entity, x => x.SafeDetailJson, SafeDetailLength);
    }

    private static void ConfigureUtcTimestamps(ModelBuilder modelBuilder)
    {
        var requiredConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var nullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
        {
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(requiredConverter);
            else if (property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(nullableConverter);
        }
    }

    private static void Required<TEntity>(EntityTypeBuilder<TEntity> entity, System.Linq.Expressions.Expression<Func<TEntity, string>> property, int maxLength)
        where TEntity : class => entity.Property(property).HasMaxLength(maxLength).IsRequired();

    private static void Concurrency<TEntity>(EntityTypeBuilder<TEntity> entity, System.Linq.Expressions.Expression<Func<TEntity, byte[]>> property)
        where TEntity : class => entity.Property(property).IsConcurrencyToken();
}
