using ValenceControl.Healing.Core;
using Microsoft.EntityFrameworkCore;
using ComponentManifestModel = ValenceControl.Healing.Core.ComponentManifest;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingDbContext(DbContextOptions<HealingDbContext> options) : DbContext(options)
{
    public DbSet<HealingConfiguration> HealingConfigurations => Set<HealingConfiguration>();
    public DbSet<HealingWorkspaceConfiguration> HealingWorkspaceConfigurations => Set<HealingWorkspaceConfiguration>();
    public DbSet<HealingEnvironmentConfiguration> HealingEnvironmentConfigurations => Set<HealingEnvironmentConfiguration>();
    public DbSet<HealingTelemetrySource> HealingTelemetrySources => Set<HealingTelemetrySource>();
    public DbSet<HealingSignalInboxItem> HealingSignalInboxItems => Set<HealingSignalInboxItem>();
    public DbSet<ComponentManifestModel> ComponentManifests => Set<ComponentManifestModel>();
    public DbSet<ComponentManifestRegistration> ComponentManifestRegistrations => Set<ComponentManifestRegistration>();
    public DbSet<ComponentManifestEntry> ComponentManifestEntries => Set<ComponentManifestEntry>();
    public DbSet<ComponentManifestAssemblyArtifact> ComponentManifestAssemblyArtifacts => Set<ComponentManifestAssemblyArtifact>();
    public DbSet<ComponentDependency> ComponentDependencies => Set<ComponentDependency>();
    public DbSet<SourceOwnershipBinding> SourceOwnershipBindings => Set<SourceOwnershipBinding>();
    public DbSet<IncidentOccurrence> IncidentOccurrences => Set<IncidentOccurrence>();
    public DbSet<ComponentAttribution> ComponentAttributions => Set<ComponentAttribution>();
    public DbSet<HealingIncident> HealingIncidents => Set<HealingIncident>();
    public DbSet<IncidentEpisode> IncidentEpisodes => Set<IncidentEpisode>();
    public DbSet<EnvironmentImpact> EnvironmentImpacts => Set<EnvironmentImpact>();
    public DbSet<RepairWorkItemProjection> RepairWorkItemProjections => Set<RepairWorkItemProjection>();
    public DbSet<RepairAttempt> RepairAttempts => Set<RepairAttempt>();
    public DbSet<ManagedRepairProposal> ManagedRepairProposals => Set<ManagedRepairProposal>();
    public DbSet<ManagedRepairInferenceReservation> ManagedRepairInferenceReservations => Set<ManagedRepairInferenceReservation>();
    public DbSet<EvidenceBundle> EvidenceBundles => Set<EvidenceBundle>();
    public DbSet<EvidenceAccessDecision> EvidenceAccessDecisions => Set<EvidenceAccessDecision>();
    public DbSet<RepairResult> RepairResults => Set<RepairResult>();
    public DbSet<RepairPullRequest> RepairPullRequests => Set<RepairPullRequest>();
    public DbSet<PathPolicy> PathPolicies => Set<PathPolicy>();
    public DbSet<EvidencePolicy> EvidencePolicies => Set<EvidencePolicy>();
    public DbSet<MergePolicy> MergePolicies => Set<MergePolicy>();
    public DbSet<PolicyEvaluation> PolicyEvaluations => Set<PolicyEvaluation>();
    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();
    public DbSet<ProviderMutationJournalEntry> ProviderMutationJournalEntries => Set<ProviderMutationJournalEntry>();
    public DbSet<WorkloadIdentityExchange> WorkloadIdentityExchanges => Set<WorkloadIdentityExchange>();
    public DbSet<WorkloadHeartbeat> WorkloadHeartbeats => Set<WorkloadHeartbeat>();
    public DbSet<ProviderWebhookDelivery> ProviderWebhookDeliveries => Set<ProviderWebhookDelivery>();
    public DbSet<HumanCommand> HumanCommands => Set<HumanCommand>();
    public DbSet<ProviderActorIdentityLink> ProviderActorIdentityLinks => Set<ProviderActorIdentityLink>();
    public DbSet<DeploymentObservation> DeploymentObservations => Set<DeploymentObservation>();
    public DbSet<VerificationResult> VerificationResults => Set<VerificationResult>();
    public DbSet<RepairVerificationFailureOutboxItem> RepairVerificationFailureOutbox => Set<RepairVerificationFailureOutboxItem>();
    internal DbSet<HealingAuditEvent> HealingAuditEvents => Set<HealingAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        HealingModelConfiguration.Configure(modelBuilder, Database.ProviderName);

    public override int SaveChanges()
    {
        EnforceAppendOnlyAudit();
        EnforceAppendOnlyEvidence();
        StampConcurrencyTokens();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyAudit();
        EnforceAppendOnlyEvidence();
        StampConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnforceAppendOnlyAudit()
    {
        if (ChangeTracker.Entries<HealingAuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Healing audit events are append-only and cannot be updated or deleted.");
    }

    private void EnforceAppendOnlyEvidence()
    {
        if (ChangeTracker.Entries<EvidenceBundle>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<EvidenceAccessDecision>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Healing evidence bundles and access decisions are append-only.");
        }
    }

    private void StampConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var property = entry.Metadata.FindProperty("Version");
            if (property?.ClrType != typeof(byte[]) || !property.IsConcurrencyToken)
                continue;
            entry.Property("Version").CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }
}
