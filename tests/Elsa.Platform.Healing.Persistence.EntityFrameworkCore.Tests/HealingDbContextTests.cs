using Elsa.Platform.Healing.Core;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ComponentManifestModel = Elsa.Platform.Healing.Core.ComponentManifest;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingDbContextTests
{
    [Fact]
    public async Task Inbox_idempotency_key_is_unique_within_workspace_and_application()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        fixture.Db.HealingSignalInboxItems.Add(CreateInboxItem(workspaceId, applicationId, "occurrence-1"));
        fixture.Db.HealingSignalInboxItems.Add(CreateInboxItem(workspaceId, applicationId, "occurrence-1"));

        var act = () => fixture.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Audit_events_cannot_be_updated_after_append()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var auditEvent = new HealingAuditEvent
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Sequence = 1,
            AggregateType = "incident",
            AggregateId = Guid.NewGuid(),
            EventType = "incident.observed",
            ReasonCode = "accepted",
            ActorType = "platform",
            ActorId = "healing-inbox",
            CorrelationId = Guid.NewGuid(),
            SafeDetailJson = "{}",
            OccurredAt = DateTimeOffset.UtcNow
        };
        fixture.Db.Set<HealingAuditEvent>().Add(auditEvent);
        await fixture.Db.SaveChangesAsync();

        auditEvent.ReasonCode = "tampered";
        var act = () => fixture.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    [Fact]
    public async Task Mutable_aggregates_reject_lost_updates_from_a_stale_context()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        try
        {
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.HealingConfigurations.Add(CreateConfiguration());
                await setup.SaveChangesAsync();
            }

            await using var first = new HealingDbContext(options);
            await using var stale = new HealingDbContext(options);
            var firstCopy = await first.HealingConfigurations.SingleAsync();
            var staleCopy = await stale.HealingConfigurations.SingleAsync();
            firstCopy.RepairEnabled = true;
            staleCopy.AutomaticMergeEnabled = true;

            await first.SaveChangesAsync();
            var act = () => stale.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Sqlite_and_sql_server_models_define_equivalent_filtered_authority_indexes()
    {
        using var sqlite = new HealingDbContext(new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        using var sqlServer = new HealingDbContext(new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlServer("Server=localhost;Database=ElsaHealing;User ID=test;Password=not-real;Encrypt=False").Options);

        Filter<HealingIncident>(sqlite).Should().Contain("Status").And.Contain("NOT IN");
        Filter<HealingIncident>(sqlServer).Should().Contain("Status").And.Contain("NOT IN");
        Filter<SourceOwnershipBinding>(sqlite).Should().Contain("Status").And.Contain("= 1");
        Filter<SourceOwnershipBinding>(sqlServer).Should().Contain("Status").And.Contain("= 1");
    }

    [Fact]
    public void Authority_foreign_keys_carry_explicit_tenant_scope()
    {
        using var db = new HealingDbContext(new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite("Data Source=:memory:").Options);

        ForeignKeyProperties<ComponentAttribution, IncidentOccurrence>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "OccurrenceId");
        ForeignKeyProperties<ComponentAttribution, SourceOwnershipBinding>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "BindingId");
        ForeignKeyProperties<RepairAttempt, HealingIncident>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "IncidentId");
        ForeignKeyProperties<RepairAttempt, SourceOwnershipBinding>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "BindingId");
        ForeignKeyProperties<PolicyEvaluation, HealingPolicyDefinition>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "PolicyId");
        ForeignKeyProperties<ProviderOperation, ProviderConnection>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ProviderConnectionId");
        ForeignKeyProperties<ProviderOperation, RepairAttempt>(db)
            .Should().BeEquivalentTo("WorkspaceId", "ApplicationId", "AttemptId");
    }

    [Fact]
    public async Task Revoked_binding_name_can_be_reused_but_two_active_bindings_cannot_coexist()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var first = CreateBinding(SourceOwnershipBindingStatus.Active);
        SeedBindingAuthorities(fixture.Db, first);
        fixture.Db.SourceOwnershipBindings.Add(first);
        await fixture.Db.SaveChangesAsync();
        first.Status = SourceOwnershipBindingStatus.Revoked;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.SourceOwnershipBindings.Add(CreateBinding(SourceOwnershipBindingStatus.Active, first));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.SourceOwnershipBindings.Add(CreateBinding(SourceOwnershipBindingStatus.Active, first));

        var act = () => fixture.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Active_binding_cannot_reference_nonexistent_mutation_authority()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        fixture.Db.SourceOwnershipBindings.Add(CreateBinding(SourceOwnershipBindingStatus.Active));

        var act = () => fixture.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Component_dependency_cannot_cross_manifest_boundary()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var firstManifest = CreateManifest(Guid.NewGuid());
        var secondManifest = CreateManifest(Guid.NewGuid());
        var firstEntry = CreateManifestEntry(firstManifest, "first");
        var secondEntry = CreateManifestEntry(secondManifest, "second");
        firstManifest.Entries.Add(firstEntry);
        secondManifest.Entries.Add(secondEntry);
        fixture.Db.ComponentManifests.AddRange(firstManifest, secondManifest);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ComponentDependencies.Add(new ComponentDependency
        {
            Id = Guid.NewGuid(),
            ManifestId = firstManifest.Id,
            FromEntryId = firstEntry.Id,
            ToEntryId = secondEntry.Id
        });

        var act = () => fixture.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static string? Filter<TEntity>(HealingDbContext db) where TEntity : class =>
        db.Model.FindEntityType(typeof(TEntity))!.GetIndexes()
            .Single(x => x.GetFilter()?.Contains("Status", StringComparison.Ordinal) == true)
            .GetFilter();

    private static IReadOnlyList<string> ForeignKeyProperties<TDependent, TPrincipal>(HealingDbContext db)
        where TDependent : class
        where TPrincipal : class =>
        db.Model.FindEntityType(typeof(TDependent))!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(TPrincipal))
            .Properties.Select(x => x.Name).ToList();

    private static HealingSignalInboxItem CreateInboxItem(Guid workspaceId, Guid applicationId, string idempotencyKey) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            EnvironmentId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = "1.0",
            OccurredAt = DateTimeOffset.UtcNow,
            AcceptedAt = DateTimeOffset.UtcNow,
            RedactedEnvelopeJson = "{}",
            EnvelopeHash = new string('a', 64),
            Status = HealingInboxStatus.Pending
        };

    private static HealingConfiguration CreateConfiguration() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        DiscoveryEnabled = true,
        SignalProfileVersion = "1.0",
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromMinutes(10),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static SourceOwnershipBinding CreateBinding(SourceOwnershipBindingStatus status, SourceOwnershipBinding? authority = null) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = new Guid("10000000-0000-0000-0000-000000000001"),
        ApplicationId = new Guid("20000000-0000-0000-0000-000000000002"),
        Name = "acme-source",
        SelectorKind = SourceSelectorKind.Package,
        SelectorPattern = "Acme.*",
        ProviderConnectionId = authority?.ProviderConnectionId ?? Guid.NewGuid(),
        RepositoryProviderId = "repository-1",
        RepositoryOwner = "acme",
        RepositoryName = "workflow-app",
        TargetBranch = "main",
        WorkflowIdentity = ".github/workflows/heal.yml",
        WorkflowRevision = "abc123",
        PathPolicyId = authority?.PathPolicyId ?? Guid.NewGuid(),
        EvidencePolicyId = authority?.EvidencePolicyId ?? Guid.NewGuid(),
        MergePolicyId = authority?.MergePolicyId ?? Guid.NewGuid(),
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static void SeedBindingAuthorities(HealingDbContext db, SourceOwnershipBinding binding)
    {
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = binding.ProviderConnectionId,
            WorkspaceId = binding.WorkspaceId,
            Provider = "github",
            InstallationId = "installation-1",
            RepositoryProviderId = binding.RepositoryProviderId,
            RepositoryOwner = binding.RepositoryOwner,
            RepositoryName = binding.RepositoryName,
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.PathPolicies.Add(new PathPolicy
        {
            Id = binding.PathPolicyId,
            WorkspaceId = binding.WorkspaceId,
            ApplicationId = binding.ApplicationId,
            Name = "path-policy",
            PolicyVersion = "1",
            PolicyHash = "path-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            AllowedRootsJson = "[]",
            ForbiddenRootsJson = "[]"
        });
        db.EvidencePolicies.Add(new EvidencePolicy
        {
            Id = binding.EvidencePolicyId,
            WorkspaceId = binding.WorkspaceId,
            ApplicationId = binding.ApplicationId,
            Name = "evidence-policy",
            PolicyVersion = "1",
            PolicyHash = "evidence-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            PermittedFieldsJson = "[]"
        });
        db.MergePolicies.Add(new MergePolicy
        {
            Id = binding.MergePolicyId,
            WorkspaceId = binding.WorkspaceId,
            ApplicationId = binding.ApplicationId,
            Name = "merge-policy",
            PolicyVersion = "1",
            PolicyHash = "merge-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            RequiredChecksJson = "[]",
            ForbiddenChangeCategoriesJson = "[]"
        });
    }

    private static ComponentManifestModel CreateManifest(Guid revisionId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        RevisionId = revisionId,
        SchemaVersion = "1.0",
        SourceRevision = Guid.NewGuid().ToString("N"),
        ManifestDigest = Guid.NewGuid().ToString("N"),
        CanonicalJson = "{}",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ComponentManifestEntry CreateManifestEntry(ComponentManifestModel manifest, string componentKey) => new()
    {
        Id = Guid.NewGuid(),
        ManifestId = manifest.Id,
        WorkspaceId = manifest.WorkspaceId,
        ApplicationId = manifest.ApplicationId,
        ComponentKey = componentKey,
        Kind = ComponentKind.Package,
        Name = componentKey,
        ContentHash = Guid.NewGuid().ToString("N"),
        RelativePath = $"packages/{componentKey}.dll"
    };

}
