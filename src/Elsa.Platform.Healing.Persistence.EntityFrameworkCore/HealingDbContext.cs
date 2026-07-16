using Elsa.Platform.Healing.Core;
using Microsoft.EntityFrameworkCore;
using ComponentManifestModel = Elsa.Platform.Healing.Core.ComponentManifest;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

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
    public DbSet<WorkloadIdentityExchange> WorkloadIdentityExchanges => Set<WorkloadIdentityExchange>();
    public DbSet<ProviderWebhookDelivery> ProviderWebhookDeliveries => Set<ProviderWebhookDelivery>();
    public DbSet<HumanCommand> HumanCommands => Set<HumanCommand>();
    public DbSet<DeploymentObservation> DeploymentObservations => Set<DeploymentObservation>();
    public DbSet<VerificationResult> VerificationResults => Set<VerificationResult>();
    internal DbSet<HealingAuditEvent> HealingAuditEvents => Set<HealingAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        HealingModelConfiguration.Configure(modelBuilder, Database.ProviderName);

    public override int SaveChanges()
    {
        EnforceAppendOnlyAudit();
        StampConcurrencyTokens();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyAudit();
        StampConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnforceAppendOnlyAudit()
    {
        if (ChangeTracker.Entries<HealingAuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Healing audit events are append-only and cannot be updated or deleted.");
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
