using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Incidents;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ComponentManifestModel = Elsa.Platform.Healing.Core.ComponentManifest;
using ComponentManifestEntryModel = Elsa.Platform.Healing.Core.ComponentManifestEntry;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IncidentProjectionConcurrencyTests
{
    [Fact]
    public async Task Concurrent_attempts_for_different_incidents_share_the_application_budget_atomically()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-admission-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30;Pooling=False")
            .Options;
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        try
        {
            RepairAttempt[] attempts;
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var authority = await SeedAuthorityAsync(setup, workspaceId, applicationId);
                setup.HealingConfigurations.Add(new HealingConfiguration
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    ApplicationId = applicationId,
                    DiscoveryEnabled = true,
                    RepairEnabled = true,
                    SignalProfileVersion = "1.0",
                    DefaultAttemptLimit = 2,
                    VerificationWindow = TimeSpan.FromMinutes(10),
                    TimeBudget = TimeSpan.FromMinutes(10),
                    ConcurrencyBudget = 1,
                    InferenceBudget = 1_000,
                    RepositoryRunBudget = 2,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                attempts = Enumerable.Range(0, 2).Select(_ =>
                {
                    var incident = new HealingIncident
                    {
                        Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                        FingerprintVersion = "1", Fingerprint = Guid.NewGuid().ToString("N"),
                        RepairRepositoryKey = "github:repository-1", Status = HealingIncidentStatus.ReadyForRepair,
                        FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow, OccurrenceCount = 1,
                        SelectedBindingId = authority.BindingId
                    };
                    var episode = new IncidentEpisode
                    {
                        Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                        IncidentId = incident.Id, OpenedAt = DateTimeOffset.UtcNow, ProducingRevisionsJson = "[]",
                        Outcome = IncidentEpisodeOutcome.Active
                    };
                    var evidence = new EvidenceBundle
                    {
                        Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                        IncidentId = incident.Id, Tier = EvidenceTier.DefaultRedacted, CanonicalJson = "{}",
                        Digest = new string('a', 64), ProvenanceJson = "{}", OmissionsJson = "[]",
                        CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                    };
                    setup.AddRange(incident, episode, evidence);
                    return new RepairAttempt
                    {
                        Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
                        IncidentId = incident.Id, EpisodeId = episode.Id, BindingId = authority.BindingId,
                        TargetRevision = new string('b', 40), Status = RepairAttemptStatus.Queued,
                        EvidenceBundleId = evidence.Id, RepairClassification = RepairClassification.InsufficientConfidence,
                        NonceHash = Guid.NewGuid().ToString("N").PadRight(64, '0'), BudgetJson = "{}", UsageJson = "{}"
                    };
                }).ToArray();
                await setup.SaveChangesAsync();
            }

            var participantsReady = 0;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<RepairAttemptStoreCreateResult> AdmitAsync(RepairAttempt attempt)
            {
                if (Interlocked.Increment(ref participantsReady) == attempts.Length)
                    start.SetResult();
                await start.Task;
                await using var db = new HealingDbContext(options);
                return await new HealingStore(db).TryCreateAttemptAsync(attempt, maximumAttempts: 2, maximumConcurrentAttempts: 1);
            }

            var results = await Task.WhenAll(attempts.Select(AdmitAsync));

            results.Select(x => x.Outcome).Should().BeEquivalentTo(new[]
            {
                RepairAttemptStoreOutcome.Created,
                RepairAttemptStoreOutcome.ConcurrencyLimitReached
            });
            await using var verify = new HealingDbContext(options);
            (await verify.RepairAttempts.CountAsync()).Should().Be(1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_occurrences_create_one_active_incident_and_preserve_exact_environment_impact()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-incidents-{Guid.NewGuid():N}.db");
        var options = Options(databasePath);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environments = new[] { Guid.NewGuid(), Guid.NewGuid() };
        try
        {
            HealingIncidentProjectionRequest[] requests;
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var authority = await SeedAuthorityAsync(setup, workspaceId, applicationId);
                requests = Enumerable.Range(0, 200)
                    .Select(index => WithAuthority(
                        Request(workspaceId, applicationId, environments[index % environments.Length], index),
                        authority))
                    .ToArray();
                setup.HealingSignalInboxItems.AddRange(requests.Select(Inbox));
                await setup.SaveChangesAsync();
            }
            await Parallel.ForEachAsync(requests, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (request, cancellationToken) =>
            {
                await using var db = new HealingDbContext(options);
                await new HealingStore(db).ProjectOccurrenceAsync(request, cancellationToken);
            });

            await using var verify = new HealingDbContext(options);
            var incident = await verify.HealingIncidents.SingleAsync();
            incident.OccurrenceCount.Should().Be(requests.Length);
            incident.Status.Should().Be(HealingIncidentStatus.ThresholdPending);
            (await verify.IncidentOccurrences.CountAsync()).Should().Be(requests.Length);
            var impacts = await verify.EnvironmentImpacts.OrderBy(x => x.EnvironmentId).ToListAsync();
            impacts.Should().HaveCount(2);
            impacts.Should().OnlyContain(x => x.OccurrenceCount == requests.Length / 2);
            impacts.Should().OnlyContain(x => x.OccurrenceThreshold == 3 && x.DebounceWindow == TimeSpan.FromMinutes(5));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Occurrence_replay_is_idempotent_and_threshold_promotion_is_due_time_guarded()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var request = Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0) with
        {
            OccurrenceThreshold = 1,
            DebounceWindow = TimeSpan.FromMinutes(5)
        };
        var authority = await SeedAuthorityAsync(fixture.Db, request.WorkspaceId, request.ApplicationId);
        request = WithAuthority(request, authority);
        fixture.Db.HealingSignalInboxItems.Add(Inbox(request));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);

        var first = await store.ProjectOccurrenceAsync(request);
        var replay = await store.ProjectOccurrenceAsync(request);
        var early = await store.PromoteDueIncidentsAsync(request.AcceptedAt.AddMinutes(4), 10);
        var due = await store.PromoteDueIncidentsAsync(request.AcceptedAt.AddMinutes(5), 10);

        first.IsReplay.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        replay.Incident.Id.Should().Be(first.Incident.Id);
        early.Should().Be(0);
        due.Should().Be(1);
        (await fixture.Db.HealingIncidents.AsNoTracking().SingleAsync()).Status
            .Should().Be(HealingIncidentStatus.ReadyForRepair);
        (await fixture.Db.RepairWorkItemProjections.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(HealingIncidentStatus.Repairing)]
    [InlineData(HealingIncidentStatus.PullRequestOpen)]
    [InlineData(HealingIncidentStatus.NeedsHuman)]
    [InlineData(HealingIncidentStatus.Merged)]
    [InlineData(HealingIncidentStatus.Verifying)]
    [InlineData(HealingIncidentStatus.FailedVerification)]
    [InlineData(HealingIncidentStatus.Suppressed)]
    public async Task New_occurrences_do_not_regress_advanced_incident_states(
        HealingIncidentStatus advancedStatus)
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var firstRequest = Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0) with
        {
            OccurrenceThreshold = 1,
            DebounceWindow = TimeSpan.Zero
        };
        var authority = await SeedAuthorityAsync(fixture.Db, firstRequest.WorkspaceId, firstRequest.ApplicationId);
        firstRequest = WithAuthority(firstRequest, authority);
        var nextRequest = firstRequest with
        {
            InboxItemId = Guid.NewGuid(),
            OccurrenceKey = "advanced-state-occurrence",
            AcceptedAt = firstRequest.AcceptedAt.AddMinutes(1),
            OccurredAt = firstRequest.OccurredAt.AddMinutes(1)
        };
        fixture.Db.HealingSignalInboxItems.AddRange(Inbox(firstRequest), Inbox(nextRequest));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);

        var first = await store.ProjectOccurrenceAsync(firstRequest);
        first.Incident.Status = advancedStatus;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var projected = await store.ProjectOccurrenceAsync(nextRequest);

        projected.Incident.Status.Should().Be(advancedStatus);
        (await fixture.Db.HealingIncidents.AsNoTracking().SingleAsync()).Status.Should().Be(advancedStatus);
        (await fixture.Db.IncidentOccurrences.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_due_promoters_create_exactly_one_work_item_projection()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-healing-promotion-{Guid.NewGuid():N}.db");
        var options = Options(databasePath);
        var request = Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0) with
        {
            OccurrenceThreshold = 1,
            DebounceWindow = TimeSpan.FromMinutes(5)
        };
        try
        {
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                request = WithAuthority(request, await SeedAuthorityAsync(setup, request.WorkspaceId, request.ApplicationId));
                setup.HealingSignalInboxItems.Add(Inbox(request));
                await setup.SaveChangesAsync();
                await new HealingStore(setup).ProjectOccurrenceAsync(request);
            }

            var dueAt = request.AcceptedAt.Add(request.DebounceWindow);
            var promotions = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var db = new HealingDbContext(options);
                return await new HealingStore(db).PromoteDueIncidentsAsync(dueAt, 10);
            }));

            promotions.Sum().Should().Be(1);
            await using var verify = new HealingDbContext(options);
            (await verify.RepairWorkItemProjections.CountAsync()).Should().Be(1);
            (await verify.HealingIncidents.SingleAsync()).Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Environment_thresholds_are_evaluated_from_that_environments_own_impact()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var firstEnvironment = Guid.NewGuid();
        var secondEnvironment = Guid.NewGuid();
        var authority = await SeedAuthorityAsync(fixture.Db, workspaceId, applicationId);
        var requests = new[]
        {
            WithAuthority(Request(workspaceId, applicationId, firstEnvironment, 0), authority),
            WithAuthority(Request(workspaceId, applicationId, firstEnvironment, 1), authority),
            WithAuthority(Request(workspaceId, applicationId, secondEnvironment, 2), authority),
            WithAuthority(Request(workspaceId, applicationId, firstEnvironment, 3), authority)
        };
        fixture.Db.HealingSignalInboxItems.AddRange(requests.Select(Inbox));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);

        await store.ProjectOccurrenceAsync(requests[0]);
        await store.ProjectOccurrenceAsync(requests[1]);
        var beforeIndependentThreshold = await store.ProjectOccurrenceAsync(requests[2]);
        var readyBeforeIndependentThreshold = beforeIndependentThreshold.Incident.ReadyAfter;
        var afterIndependentThreshold = await store.ProjectOccurrenceAsync(requests[3]);

        beforeIndependentThreshold.Incident.Status.Should().Be(HealingIncidentStatus.ThresholdPending);
        readyBeforeIndependentThreshold.Should().BeNull();
        afterIndependentThreshold.Incident.Status.Should().Be(HealingIncidentStatus.ThresholdPending);
        afterIndependentThreshold.Incident.ReadyAfter.Should().Be(requests[3].AcceptedAt.AddMinutes(5));
        var impacts = await fixture.Db.EnvironmentImpacts.AsNoTracking().ToArrayAsync();
        impacts.Single(x => x.EnvironmentId == secondEnvironment).ThresholdReachedAt.Should().BeNull();
        impacts.Single(x => x.EnvironmentId == firstEnvironment).ThresholdReachedAt.Should().Be(requests[3].AcceptedAt);
    }

    [Fact]
    public async Task Recurrence_after_terminal_incident_creates_a_linked_episode_without_rewriting_history()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var firstRequest = Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0) with
        {
            OccurrenceThreshold = 1,
            DebounceWindow = TimeSpan.Zero
        };
        fixture.Db.HealingSignalInboxItems.Add(Inbox(firstRequest));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var first = await store.ProjectOccurrenceAsync(firstRequest);
        var historical = await fixture.Db.HealingIncidents.SingleAsync(x => x.Id == first.Incident.Id);
        var historicalEpisode = await fixture.Db.IncidentEpisodes.SingleAsync(x => x.Id == first.Episode.Id);
        historical.Status = HealingIncidentStatus.Healed;
        historicalEpisode.Outcome = IncidentEpisodeOutcome.Healed;
        historicalEpisode.ClosedAt = firstRequest.AcceptedAt.AddHours(1);
        await fixture.Db.SaveChangesAsync();

        var recurrenceRequest = Request(
            firstRequest.WorkspaceId,
            firstRequest.ApplicationId,
            firstRequest.EnvironmentId,
            1) with
        {
            OccurrenceThreshold = 1,
            DebounceWindow = TimeSpan.Zero,
            RevisionId = Guid.NewGuid(),
            AcceptedAt = firstRequest.AcceptedAt.AddHours(2),
            OccurredAt = firstRequest.OccurredAt.AddHours(2)
        };
        fixture.Db.HealingSignalInboxItems.Add(Inbox(recurrenceRequest));
        await fixture.Db.SaveChangesAsync();

        var recurrence = await store.ProjectOccurrenceAsync(recurrenceRequest);

        recurrence.IsRegression.Should().BeTrue();
        recurrence.Incident.Id.Should().NotBe(first.Incident.Id);
        recurrence.Episode.PreviousEpisodeId.Should().Be(first.Episode.Id);
        (await fixture.Db.HealingIncidents.AsNoTracking().SingleAsync(x => x.Id == first.Incident.Id)).Status
            .Should().Be(HealingIncidentStatus.Healed);
        (await fixture.Db.IncidentEpisodes.AsNoTracking().SingleAsync(x => x.Id == first.Episode.Id)).Outcome
            .Should().Be(IncidentEpisodeOutcome.Healed);
    }

    [Fact]
    public async Task Identical_fingerprints_are_isolated_by_application()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var first = Request(workspaceId, Guid.NewGuid(), Guid.NewGuid(), 0);
        var second = Request(workspaceId, Guid.NewGuid(), Guid.NewGuid(), 1);
        fixture.Db.HealingSignalInboxItems.AddRange(Inbox(first), Inbox(second));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);

        await store.ProjectOccurrenceAsync(first);
        await store.ProjectOccurrenceAsync(second);

        (await fixture.Db.HealingIncidents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Inbox_worker_projects_eligible_signals_and_audits_excluded_failures_without_occurrences()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var authority = await SeedAuthorityAsync(fixture.Db, workspaceId, applicationId);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        fixture.Db.HealingConfigurations.Add(new HealingConfiguration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            DiscoveryEnabled = true,
            RepairEnabled = true,
            SignalProfileVersion = "1.0",
            DefaultAttemptLimit = 2,
            VerificationWindow = TimeSpan.FromMinutes(15),
            TimeBudget = TimeSpan.FromMinutes(30),
            ConcurrencyBudget = 1,
            ClassificationPolicyJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            Environments =
            [
                new HealingEnvironmentConfiguration
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    ApplicationId = applicationId,
                    EnvironmentId = environmentId,
                    DiscoveryEnabled = true,
                    RepairEnabled = true,
                    OccurrenceThreshold = 1,
                    DebounceWindow = TimeSpan.Zero,
                    ClassificationPolicyJson = "{}",
                    CreatedAt = now,
                    UpdatedAt = now
                }
            ]
        });
        var missingWorkspace = Signal(applicationId, environmentId, authority, "missing-workspace-1", HealingFailureClasses.UnhandledRequest);
        var eligible = Signal(applicationId, environmentId, authority, "eligible-1", HealingFailureClasses.UnhandledRequest);
        var excluded = Signal(applicationId, environmentId, authority, "excluded-1", HealingFailureClasses.Validation);
        fixture.Db.HealingSignalInboxItems.AddRange(
            Inbox(workspaceId, missingWorkspace, now.AddMinutes(-1)),
            Inbox(workspaceId, eligible, now),
            Inbox(workspaceId, excluded, now));
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var timeProvider = new FixedTimeProvider(now.AddMinutes(1));
        var audit = new HealingAuditService(store, timeProvider);
        var ownership = new SourceOwnershipService(store, audit, timeProvider);
        var worker = new HealingSignalInboxWorker(
            store,
            store,
            new HealingSignalNormalizer(),
            new HealingSignalClassifier(),
            new ComponentAttributionService(store, ownership),
            new HealingFingerprintService(),
            new HealingIncidentService(store),
            audit,
            new HealingKillSwitch(new HealingOptions()),
            Microsoft.Extensions.Options.Options.Create(new HealingOptions()),
            timeProvider);

        var missingWorkspaceResult = await worker.RunOnceAsync("worker-1");
        await store.UpsertWorkspaceConfigurationAsync(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            WorkspaceKillSwitch = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        var first = await worker.RunOnceAsync("worker-1");
        var second = await worker.RunOnceAsync("worker-1");
        var workspaceConfiguration = (await store.GetWorkspaceConfigurationAsync(workspaceId))!;
        workspaceConfiguration.WorkspaceKillSwitch = true;
        workspaceConfiguration.UpdatedAt = now.AddMinutes(1);
        await store.UpsertWorkspaceConfigurationAsync(workspaceConfiguration);
        fixture.Db.HealingSignalInboxItems.Add(Inbox(
            workspaceId,
            Signal(applicationId, environmentId, authority, "workspace-stopped-1", HealingFailureClasses.UnhandledRequest),
            now));
        await fixture.Db.SaveChangesAsync();
        var stopped = await worker.RunOnceAsync("worker-1");

        new[] { first.Status, second.Status }.Should().BeEquivalentTo(
            new[] { HealingInboxWorkerStatus.Projected, HealingInboxWorkerStatus.Rejected });
        missingWorkspaceResult.Status.Should().Be(HealingInboxWorkerStatus.Rejected);
        missingWorkspaceResult.OutcomeCode.Should().Be(HealingGateReasonCodes.WorkspaceConfigurationNotFound);
        stopped.Status.Should().Be(HealingInboxWorkerStatus.Rejected);
        stopped.OutcomeCode.Should().Be(HealingGateReasonCodes.WorkspaceKillSwitch);
        (await fixture.Db.IncidentOccurrences.CountAsync()).Should().Be(1);
        (await fixture.Db.HealingIncidents.SingleAsync()).Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
        (await fixture.Db.RepairWorkItemProjections.CountAsync()).Should().Be(1);
        (await store.QueryAsync(new Elsa.Platform.Healing.Core.Security.HealingAuditQuery(workspaceId))).Select(x => x.EventType)
            .Should().Contain(["occurrence-projected", "candidate-rejected"]);
    }

    [Fact]
    public async Task Disabled_incident_review_leaves_discovered_inbox_items_pending()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        fixture.Db.HealingSignalInboxItems.Add(new HealingSignalInboxItem
        {
            Id = Guid.NewGuid(), WorkspaceId = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), EnvironmentId = Guid.NewGuid(),
            IdempotencyKey = "review-disabled", Source = HealingSignalSource.OpenTelemetry, ProfileVersion = "1.0",
            OccurredAt = now, AcceptedAt = now, RedactedEnvelopeJson = "{}", EnvelopeHash = new string('a', 64),
            Status = HealingInboxStatus.Pending
        });
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var options = Microsoft.Extensions.Options.Options.Create(new HealingOptions { IncidentReviewEnabled = false });
        var time = new FixedTimeProvider(now);
        var audit = new HealingAuditService(store, time);
        var worker = new HealingSignalInboxWorker(
            store, store, new HealingSignalNormalizer(), new HealingSignalClassifier(),
            new ComponentAttributionService(store, new SourceOwnershipService(store, audit, time)),
            new HealingFingerprintService(), new HealingIncidentService(store), audit,
            new HealingKillSwitch(options.Value), options, time);

        var result = await worker.RunOnceAsync("review-disabled-worker");

        result.Should().Be(new HealingInboxWorkerResult(
            HealingInboxWorkerStatus.Idle, OutcomeCode: HealingGateReasonCodes.StageDisabled));
        var pending = await fixture.Db.HealingSignalInboxItems.AsNoTracking().SingleAsync();
        pending.Status.Should().Be(HealingInboxStatus.Pending);
        pending.AttemptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, false, HealingInboxWorkerStatus.RetryScheduled, HealingInboxStatus.Pending, "processing-failed")]
    [InlineData(2, false, HealingInboxWorkerStatus.DeadLettered, HealingInboxStatus.DeadLettered, "processing-attempt-limit")]
    [InlineData(0, true, HealingInboxWorkerStatus.LeaseLost, HealingInboxStatus.Leased, "lease-lost")]
    [InlineData(2, true, HealingInboxWorkerStatus.LeaseLost, HealingInboxStatus.Leased, "lease-lost")]
    public async Task Inbox_worker_persists_bounded_failure_outcomes_and_never_claims_a_lost_lease(
        int initialAttemptCount,
        bool expireLease,
        HealingInboxWorkerStatus expectedWorkerStatus,
        HealingInboxStatus expectedInboxStatus,
        string expectedOutcomeCode)
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        fixture.Db.HealingSignalInboxItems.Add(new HealingSignalInboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            IdempotencyKey = $"invalid-{Guid.NewGuid():N}",
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = HealingContractVersions.SignalProfile,
            OccurredAt = now,
            AcceptedAt = now,
            RedactedEnvelopeJson = "{invalid-json",
            EnvelopeHash = $"sha256:{new string('f', 64)}",
            Status = HealingInboxStatus.Pending,
            AttemptCount = initialAttemptCount
        });
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var timeProvider = new LeaseOutcomeTimeProvider(now, expireLease);
        var audit = new HealingAuditService(store, timeProvider);
        var ownership = new SourceOwnershipService(store, audit, timeProvider);
        var options = Microsoft.Extensions.Options.Options.Create(new HealingOptions
        {
            LeaseDuration = TimeSpan.FromMinutes(5),
            RetryDelay = TimeSpan.FromMinutes(1)
        });
        var worker = new HealingSignalInboxWorker(
            store,
            store,
            new HealingSignalNormalizer(),
            new HealingSignalClassifier(),
            new ComponentAttributionService(store, ownership),
            new HealingFingerprintService(),
            new HealingIncidentService(store),
            audit,
            new HealingKillSwitch(options.Value),
            options,
            timeProvider);

        var result = await worker.RunOnceAsync("failure-worker");

        result.Status.Should().Be(expectedWorkerStatus);
        result.OutcomeCode.Should().Be(expectedOutcomeCode);
        var persisted = await fixture.Db.HealingSignalInboxItems.AsNoTracking().SingleAsync();
        persisted.Status.Should().Be(expectedInboxStatus);
        persisted.AttemptCount.Should().Be(initialAttemptCount + 1);
        if (expectedWorkerStatus == HealingInboxWorkerStatus.RetryScheduled)
            persisted.NextAttemptAt.Should().Be(now.AddMinutes(1));
        if (expectedWorkerStatus == HealingInboxWorkerStatus.LeaseLost)
            persisted.OutcomeCode.Should().BeNull();
        else
            persisted.OutcomeCode.Should().Be(expectedOutcomeCode);
    }

    private static HealingIncidentProjectionRequest Request(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        int index) =>
        new(
            Guid.NewGuid(),
            workspaceId,
            applicationId,
            environmentId,
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
            $"occurrence-{index}",
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
            new DateTimeOffset(2026, 7, 16, 12, 0, 1, TimeSpan.Zero).AddSeconds(index),
            IncidentClassification.UnhandledRequest,
            IncidentSeverity.Error,
            "System.InvalidOperationException",
            "checkout.order",
            "[]",
            $"{index:x32}",
            $"{index:x16}",
            IncidentRetryState.None,
            "1",
            $"sha256:{new string('a', 64)}",
            EvidenceTier.DefaultRedacted,
            $"sha256:{index:x64}",
            "observation-only",
            null,
            null,
            null,
            [],
            3,
            TimeSpan.FromMinutes(5),
            "1",
            $"sha256:{new string('b', 64)}");

    private static HealingSignalInboxItem Inbox(HealingIncidentProjectionRequest request) => new()
    {
        Id = request.InboxItemId,
        WorkspaceId = request.WorkspaceId,
        ApplicationId = request.ApplicationId,
        EnvironmentId = request.EnvironmentId,
        IdempotencyKey = request.OccurrenceKey,
        Source = HealingSignalSource.OpenTelemetry,
        ProfileVersion = "1.0",
        OccurredAt = request.OccurredAt,
        AcceptedAt = request.AcceptedAt,
        RedactedEnvelopeJson = "{}",
        EnvelopeHash = request.EvidenceDigest,
        Status = HealingInboxStatus.Leased,
        LeaseOwner = "test",
        LeaseToken = "lease",
        LeaseExpiresAt = request.AcceptedAt.AddHours(1)
    };

    private static HealingSignalInboxItem Inbox(Guid workspaceId, HealingSignal signal, DateTimeOffset acceptedAt) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = signal.ApplicationId,
        EnvironmentId = signal.EnvironmentId,
        IdempotencyKey = signal.OccurrenceId!,
        Source = HealingSignalSource.OpenTelemetry,
        ProfileVersion = signal.ProfileVersion,
        OccurredAt = signal.OccurredAt,
        AcceptedAt = acceptedAt,
        RedactedEnvelopeJson = JsonSerializer.Serialize(signal),
        EnvelopeHash = $"sha256:{new string('e', 64)}",
        Status = HealingInboxStatus.Pending
    };

    private static HealingSignal Signal(
        Guid applicationId,
        Guid environmentId,
        AuthorityIds authority,
        string occurrenceId,
        string failureClass) => new(
        HealingContractVersions.SignalProfile,
        applicationId,
        environmentId,
        authority.RevisionId,
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero),
        "checkout.order",
        failureClass,
        HealingRetryStates.None,
        new HealingExceptionEvidence(
            "System.InvalidOperationException",
            "redacted",
            null,
            [new HealingExceptionFrame("Acme.Checkout", "Acme.Checkout.OrderHandler", "HandleAsync", null, null)]),
        new HealingEvidenceMetadata(true, false, []),
        occurrenceId,
        SourceRevision: "abc123",
        ComponentManifestDigest: authority.ManifestDigest,
        ComponentKey: "package:Acme.Checkout",
        ServiceName: "checkout-api",
        ResourceIdentity: "checkout-api:instance",
        Severity: "error");

    private static DbContextOptions<HealingDbContext> Options(string databasePath) =>
        new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=1")
            .Options;

    private static async Task<AuthorityIds> SeedAuthorityAsync(
        HealingDbContext db,
        Guid workspaceId,
        Guid applicationId)
    {
        var providerId = Guid.NewGuid();
        var pathPolicyId = Guid.NewGuid();
        var evidencePolicyId = Guid.NewGuid();
        var mergePolicyId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var manifestDigest = $"sha256:{new string('c', 64)}";
        var now = new DateTimeOffset(2026, 7, 16, 11, 0, 0, TimeSpan.Zero);
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = providerId,
            WorkspaceId = workspaceId,
            Provider = "github",
            InstallationId = "installation-1",
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "checkout",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.PathPolicies.Add(new PathPolicy
        {
            Id = pathPolicyId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            Name = "path",
            PolicyVersion = "1",
            PolicyHash = "path-hash",
            CreatedAt = now,
            AllowedRootsJson = "[]",
            ForbiddenRootsJson = "[]"
        });
        db.EvidencePolicies.Add(new EvidencePolicy
        {
            Id = evidencePolicyId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            Name = "evidence",
            PolicyVersion = "1",
            PolicyHash = "evidence-hash",
            CreatedAt = now,
            PermittedFieldsJson = "[]"
        });
        db.MergePolicies.Add(new MergePolicy
        {
            Id = mergePolicyId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            Name = "merge",
            PolicyVersion = "1",
            PolicyHash = "merge-hash",
            CreatedAt = now,
            RequiredChecksJson = "[]",
            ForbiddenChangeCategoriesJson = "[]"
        });
        db.ComponentManifests.Add(new ComponentManifestModel
        {
            Id = manifestId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            RevisionId = revisionId,
            SchemaVersion = "1.0",
            SourceRevision = "abc123",
            ManifestDigest = manifestDigest,
            CanonicalJson = "{}",
            TrustState = ComponentManifestTrustState.Verified,
            VerificationMethod = "platform-managed-build-attestation",
            CreatedAt = now,
            Entries =
            [
                new ComponentManifestEntryModel
                {
                    Id = componentId,
                    ManifestId = manifestId,
                    WorkspaceId = workspaceId,
                    ApplicationId = applicationId,
                    ComponentKey = "package:Acme.Checkout",
                    Kind = ComponentKind.Package,
                    KindName = "package",
                    Name = "Acme.Checkout",
                    PackageId = "Acme.Checkout",
                    AssemblyName = "Acme.Checkout",
                    ContentHash = $"sha256:{new string('d', 64)}",
                    RelativePath = "packages/Acme.Checkout.dll"
                }
            ]
        });
        db.SourceOwnershipBindings.Add(new SourceOwnershipBinding
        {
            Id = bindingId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            Name = "checkout",
            SelectorKind = SourceSelectorKind.Package,
            SelectorPattern = "Acme.Checkout",
            ProviderConnectionId = providerId,
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "checkout",
            TargetBranch = "main",
            WorkflowIdentity = ".github/workflows/heal.yml",
            WorkflowReference = "refs/tags/elsa-healing-v1",
            WorkflowRevision = "abc123",
            PathPolicyId = pathPolicyId,
            EvidencePolicyId = evidencePolicyId,
            MergePolicyId = mergePolicyId,
            Status = SourceOwnershipBindingStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return new AuthorityIds(bindingId, componentId, providerId, revisionId, manifestDigest);
    }

    private sealed record AuthorityIds(
        Guid BindingId,
        Guid ComponentId,
        Guid ProviderConnectionId,
        Guid RevisionId,
        string ManifestDigest);

    private static HealingIncidentProjectionRequest WithAuthority(
        HealingIncidentProjectionRequest request,
        AuthorityIds authority) =>
        request with
        {
            SelectedBindingId = authority.BindingId,
            SelectedComponentEntryId = authority.ComponentId,
            ProviderConnectionId = authority.ProviderConnectionId,
            RepairRepositoryKey = "github:repository-1"
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class LeaseOutcomeTimeProvider(DateTimeOffset leaseStartedAt, bool expireLease) : TimeProvider
    {
        private int _callCount;

        public override DateTimeOffset GetUtcNow() =>
            Interlocked.Increment(ref _callCount) == 1 || !expireLease
                ? leaseStartedAt
                : leaseStartedAt.AddMinutes(6);
    }
}
