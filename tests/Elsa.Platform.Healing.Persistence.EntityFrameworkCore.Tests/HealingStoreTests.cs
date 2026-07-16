using Elsa.Platform.Healing.Core;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingStoreTests
{
    [Fact]
    public async Task Inbox_append_is_idempotent_for_matching_payload_and_rejects_key_reuse()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var item = CreateInboxItem("occurrence-1", "hash-a");

        var accepted = await store.AppendInboxAsync(item);
        var replay = await store.AppendInboxAsync(CreateInboxItem("occurrence-1", "hash-a"));
        var conflict = () => store.AppendInboxAsync(CreateInboxItem("occurrence-1", "hash-b")).AsTask();

        accepted.IsReplay.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        replay.Value.Id.Should().Be(accepted.Value.Id);
        await conflict.Should().ThrowAsync<HealingIdempotencyConflictException>();
    }

    [Fact]
    public async Task Inbox_lease_requires_its_token_and_terminal_items_are_not_requeued()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        await store.AppendInboxAsync(CreateInboxItem("occurrence-lease", "hash-lease"));

        var lease = await store.TryLeaseNextInboxAsync("worker-1", now, TimeSpan.FromMinutes(5));
        var competingLease = await store.TryLeaseNextInboxAsync("worker-2", now, TimeSpan.FromMinutes(5));
        var wrongToken = await store.CompleteInboxAsync(lease!.Value.Id, "wrong-token", now, HealingInboxStatus.Completed, "accepted", null);
        var completed = await store.CompleteInboxAsync(lease.Value.Id, lease.LeaseToken, now, HealingInboxStatus.Completed, "accepted", null);
        var terminalLease = await store.TryLeaseNextInboxAsync("worker-2", now.AddHours(1), TimeSpan.FromMinutes(5));

        competingLease.Should().BeNull();
        wrongToken.Should().BeFalse();
        completed.Should().BeTrue();
        terminalLease.Should().BeNull();
    }

    [Fact]
    public async Task Expired_inbox_lease_cannot_be_completed_by_stale_worker()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        await store.AppendInboxAsync(CreateInboxItem("occurrence-expired", "hash-expired"));
        var lease = await store.TryLeaseNextInboxAsync("worker-1", now, TimeSpan.FromMinutes(5));

        var completed = await store.CompleteInboxAsync(
            lease!.Value.Id,
            lease.LeaseToken,
            now.AddMinutes(6),
            HealingInboxStatus.Completed,
            "accepted",
            null);

        completed.Should().BeFalse();
    }

    [Fact]
    public async Task Provider_operation_is_idempotent_and_uses_expiring_token_bound_lease()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var operation = CreateProviderOperation("dispatch-1", "payload-a", now);
        fixture.Db.ProviderConnections.Add(new ProviderConnection
        {
            Id = operation.ProviderConnectionId,
            WorkspaceId = operation.WorkspaceId,
            Provider = "github",
            InstallationId = "installation-1",
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "workflow-app",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.Db.SaveChangesAsync();
        var accepted = await store.AppendProviderOperationAsync(operation);
        var replay = await store.AppendProviderOperationAsync(CreateProviderOperation("dispatch-1", "payload-a", now));
        var lease = await store.TryLeaseNextProviderOperationAsync("provider-worker", now, TimeSpan.FromMinutes(5));

        var staleCompletion = await store.CompleteProviderOperationAsync(
            lease!.Value.Id,
            lease.LeaseToken,
            now.AddMinutes(6),
            ProviderOperationStatus.Completed,
            "provider-42",
            null,
            null);

        accepted.IsReplay.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        staleCompletion.Should().BeFalse();
    }

    [Fact]
    public async Task Configuration_upsert_reconciles_environment_overrides_and_rejects_stale_version()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var firstEnvironmentId = Guid.NewGuid();
        var secondEnvironmentId = Guid.NewGuid();
        var created = await store.UpsertConfigurationAsync(CreateConfiguration(workspaceId, applicationId,
            CreateEnvironment(workspaceId, applicationId, firstEnvironmentId, false)));
        var staleVersion = created.Version.ToArray();
        var update = CreateConfiguration(workspaceId, applicationId,
            CreateEnvironment(workspaceId, applicationId, secondEnvironmentId, true));
        update.Version = created.Version.ToArray();

        await store.UpsertConfigurationAsync(update);
        var persisted = await store.GetConfigurationAsync(workspaceId, applicationId);
        var stale = CreateConfiguration(workspaceId, applicationId);
        stale.Version = staleVersion;
        var staleWrite = () => store.UpsertConfigurationAsync(stale).AsTask();

        persisted!.Environments.Should().ContainSingle()
            .Which.EnvironmentId.Should().Be(secondEnvironmentId);
        persisted.Environments.Single().RepairEnabled.Should().BeTrue();
        await staleWrite.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Identical_first_configuration_create_is_an_idempotent_replay()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var accepted = await store.UpsertConfigurationAsync(CreateConfiguration(
            workspaceId,
            applicationId,
            CreateEnvironment(workspaceId, applicationId, environmentId, true)));
        var replay = CreateConfiguration(
            workspaceId,
            applicationId,
            CreateEnvironment(workspaceId, applicationId, environmentId, true));

        var result = await store.UpsertConfigurationAsync(replay);
        replay.RepairEnabled = true;
        var conflict = () => store.UpsertConfigurationAsync(replay).AsTask();

        result.Id.Should().Be(accepted.Id);
        await conflict.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Identical_first_verification_create_is_an_idempotent_replay()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incident = CreateIncident(workspaceId, applicationId);
        var episode = CreateEpisode(workspaceId, applicationId, incident.Id);
        var environmentId = Guid.NewGuid();
        fixture.Db.HealingIncidents.Add(incident);
        fixture.Db.IncidentEpisodes.Add(episode);
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var accepted = CreateVerification(workspaceId, applicationId, episode.Id, environmentId, VerificationOutcome.Deployed);
        await store.UpsertVerificationAsync(accepted);
        var replay = CreateVerification(workspaceId, applicationId, episode.Id, environmentId, VerificationOutcome.Deployed);

        var result = await store.UpsertVerificationAsync(replay);
        replay.Outcome = VerificationOutcome.Healed;
        var conflict = () => store.UpsertVerificationAsync(replay).AsTask();

        result.Id.Should().Be(accepted.Id);
        await conflict.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Manifest_revision_is_idempotent_and_conflicting_graph_is_fully_detached()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var accepted = CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a");
        await store.AppendManifestAsync(accepted);
        var replay = await store.AppendManifestAsync(CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-b"));
        var conflicting = CreateManifest(workspaceId, applicationId, revisionId, "digest-b", "component-c");

        var conflict = () => store.AppendManifestAsync(conflicting).AsTask();

        replay.IsReplay.Should().BeTrue();
        await conflict.Should().ThrowAsync<HealingIdempotencyConflictException>();
        var addedGraphEntries = fixture.Db.ChangeTracker.Entries()
            .Where(x => x.State == EntityState.Added &&
                        (x.Entity == conflicting ||
                         x.Entity is ComponentManifestEntry entry && conflicting.Entries.Contains(entry)))
            .ToList();
        addedGraphEntries.Should().BeEmpty();
        await fixture.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Workspace_kill_switch_configuration_is_unique_and_versioned()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var created = await store.UpsertWorkspaceConfigurationAsync(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var staleVersion = created.Version.ToArray();
        var update = new HealingWorkspaceConfiguration
        {
            WorkspaceId = workspaceId,
            WorkspaceKillSwitch = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = created.Version.ToArray()
        };

        await store.UpsertWorkspaceConfigurationAsync(update);
        update.Version = staleVersion;
        var stale = () => store.UpsertWorkspaceConfigurationAsync(update).AsTask();

        (await store.GetWorkspaceConfigurationAsync(workspaceId))!.WorkspaceKillSwitch.Should().BeTrue();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Concurrent_audit_appends_allocate_unique_monotonic_aggregate_sequences()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-audit-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=30";
        var workspaceId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        try
        {
            await using (var setup = CreateFileContext(connectionString))
                await setup.Database.EnsureCreatedAsync();

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
            {
                await start.Task;
                await using var db = CreateFileContext(connectionString);
                await new HealingStore(db).AppendAsync(CreateAuditEvent(workspaceId, aggregateId, index));
            })).ToArray();
            start.SetResult();
            await Task.WhenAll(writes);

            await using var verification = CreateFileContext(connectionString);
            var sequences = await verification.Set<HealingAuditEvent>().AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.AggregateId == aggregateId)
                .OrderBy(x => x.Sequence)
                .Select(x => x.Sequence)
                .ToListAsync();
            sequences.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static HealingSignalInboxItem CreateInboxItem(string idempotencyKey, string hash) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = new Guid("10000000-0000-0000-0000-000000000001"),
            ApplicationId = new Guid("20000000-0000-0000-0000-000000000002"),
            EnvironmentId = new Guid("30000000-0000-0000-0000-000000000003"),
            IdempotencyKey = idempotencyKey,
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = "1.0",
            OccurredAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
            AcceptedAt = DateTimeOffset.Parse("2026-07-16T10:00:01Z"),
            RedactedEnvelopeJson = "{}",
            EnvelopeHash = hash,
            Status = HealingInboxStatus.Pending
        };

    private static ProviderOperation CreateProviderOperation(string idempotencyKey, string payloadHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = new Guid("10000000-0000-0000-0000-000000000001"),
            ApplicationId = new Guid("20000000-0000-0000-0000-000000000002"),
            ProviderConnectionId = new Guid("40000000-0000-0000-0000-000000000004"),
            Kind = ProviderOperationKind.DispatchWorkflow,
            IdempotencyKey = idempotencyKey,
            PayloadJson = "{}",
            PayloadHash = payloadHash,
            Status = ProviderOperationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static HealingConfiguration CreateConfiguration(
        Guid workspaceId,
        Guid applicationId,
        params HealingEnvironmentConfiguration[] environments) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        DiscoveryEnabled = true,
        SignalProfileVersion = "1.0",
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromMinutes(10),
        CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        Environments = [.. environments]
    };

    private static HealingEnvironmentConfiguration CreateEnvironment(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        bool repairEnabled) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        EnvironmentId = environmentId,
        RepairEnabled = repairEnabled,
        CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z")
    };

    private static ComponentManifest CreateManifest(
        Guid workspaceId,
        Guid applicationId,
        Guid revisionId,
        string digest,
        string componentKey)
    {
        var manifest = new ComponentManifest
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            RevisionId = revisionId,
            SchemaVersion = "1.0",
            SourceRevision = "abc123",
            ManifestDigest = digest,
            CanonicalJson = "{}",
            CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z")
        };
        manifest.Entries.Add(new ComponentManifestEntry
        {
            Id = Guid.NewGuid(),
            ManifestId = manifest.Id,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            ComponentKey = componentKey,
            Kind = ComponentKind.Package,
            Name = componentKey,
            ContentHash = "hash",
            RelativePath = $"packages/{componentKey}.dll"
        });
        return manifest;
    }

    private static HealingDbContext CreateFileContext(string connectionString) => new(
        new DbContextOptionsBuilder<HealingDbContext>().UseSqlite(connectionString).Options);

    private static HealingAuditEvent CreateAuditEvent(Guid workspaceId, Guid aggregateId, int index) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        AggregateType = "incident",
        AggregateId = aggregateId,
        EventType = "incident.observed",
        ReasonCode = $"accepted-{index}",
        ActorType = "platform",
        ActorId = "healing-inbox",
        CorrelationId = Guid.NewGuid(),
        SafeDetailJson = "{}",
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static HealingIncident CreateIncident(Guid workspaceId, Guid applicationId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        FingerprintVersion = "1",
        Fingerprint = Guid.NewGuid().ToString("N"),
        RepairRepositoryKey = "observation-only",
        Status = HealingIncidentStatus.Observed,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
        OccurrenceCount = 1
    };

    private static IncidentEpisode CreateEpisode(Guid workspaceId, Guid applicationId, Guid incidentId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        IncidentId = incidentId,
        OpenedAt = DateTimeOffset.UtcNow,
        ProducingRevisionsJson = "[]",
        Outcome = IncidentEpisodeOutcome.Active
    };

    private static VerificationResult CreateVerification(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        Guid environmentId,
        VerificationOutcome outcome) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        EpisodeId = episodeId,
        EnvironmentId = environmentId,
        RepairedRevision = "abc123",
        Outcome = outcome
    };
}
