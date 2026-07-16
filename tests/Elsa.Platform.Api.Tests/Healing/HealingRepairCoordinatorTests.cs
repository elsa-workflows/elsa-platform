using System.Text.Json;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class HealingRepairCoordinatorTests
{
    [Fact]
    public async Task Pending_projection_queues_one_versioned_issue_operation()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Pending);

        var first = await database.RunCoordinatorAsync();
        var replay = await database.RunCoordinatorAsync();

        await using var verify = database.CreateContext();
        var operation = await verify.ProviderOperations.SingleAsync();
        var request = JsonSerializer.Deserialize<RepairWorkItemUpsertRequest>(operation.PayloadJson);
        first.Should().Be(HealingRepairCoordinatorStatus.WorkItemQueued);
        replay.Should().Be(HealingRepairCoordinatorStatus.Idle);
        operation.Kind.Should().Be(ProviderOperationKind.UpsertWorkItem);
        request.Should().NotBeNull();
        request!.ProtocolVersion.Should().Be(HealingContractVersions.ProviderProtocol);
        request.IdempotencyKey.Should().StartWith($"work-item:{database.Ids.ProjectionId:N}:");
    }

    [Fact]
    public async Task Concurrent_current_projection_runs_create_at_most_one_attempt_and_protect_the_dispatch_nonce()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Current);

        var results = await Task.WhenAll(
            database.RunCoordinatorAsync().AsTask(),
            database.RunCoordinatorAsync().AsTask());

        await using var verify = database.CreateContext();
        var attempt = await verify.RepairAttempts.SingleAsync();
        var dispatch = await verify.ProviderOperations.SingleAsync(x => x.Kind == ProviderOperationKind.DispatchWorkflow);
        var request = JsonSerializer.Deserialize<RepairWorkflowDispatchRequest>(dispatch.PayloadJson);
        results.Count(x => x == HealingRepairCoordinatorStatus.RepairQueued).Should().Be(1);
        attempt.NonceHash.Should().MatchRegex("^[0-9a-f]{64}$");
        request.Should().NotBeNull();
        request!.OneTimeNonce.Should().StartWith("dp:");
        var rawNonce = database.UnprotectDispatchNonce(request.OneTimeNonce);
        rawNonce.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        rawNonce.Should().NotBe(attempt.NonceHash);
        request.OneTimeNonce.Should().NotContain(attempt.NonceHash);
        dispatch.PayloadJson.Should().NotContain(rawNonce);
        dispatch.PayloadJson.Should().NotContain(attempt.NonceHash);
    }

    [Fact]
    public async Task Abandoned_first_attempt_gets_one_retry_then_attempt_cap_marks_incident_needs_human()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Current);
        await database.SeedAbandonedAttemptAsync(attemptNumber: 1);

        var retry = await database.RunCoordinatorAsync();
        await database.AbandonLatestAttemptAsync();
        var capped = await database.RunCoordinatorAsync();

        await using var verify = database.CreateContext();
        var attempts = await verify.RepairAttempts.OrderBy(x => x.AttemptNumber).ToArrayAsync();
        var incident = await verify.HealingIncidents.SingleAsync();
        retry.Should().Be(HealingRepairCoordinatorStatus.RepairQueued);
        capped.Should().Be(HealingRepairCoordinatorStatus.Idle);
        attempts.Should().HaveCount(2);
        attempts.Should().OnlyContain(x => x.Status == RepairAttemptStatus.Failed);
        incident.Status.Should().Be(HealingIncidentStatus.NeedsHuman);
        incident.NeedsHumanReason.Should().Be(NeedsHumanReason.AttemptLimitReached);
    }

    [Fact]
    public async Task Application_attempt_limit_lower_than_global_limit_is_effective()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Current);
        await database.SetApplicationAttemptLimitAsync(1);
        await database.SeedAbandonedAttemptAsync(attemptNumber: 1);

        var result = await database.RunCoordinatorAsync();

        await using var verify = database.CreateContext();
        var incident = await verify.HealingIncidents.SingleAsync();
        result.Should().Be(HealingRepairCoordinatorStatus.Idle);
        (await verify.RepairAttempts.CountAsync()).Should().Be(1);
        incident.Status.Should().Be(HealingIncidentStatus.NeedsHuman);
        incident.NeedsHumanReason.Should().Be(NeedsHumanReason.AttemptLimitReached);
    }

    [Fact]
    public async Task Result_received_with_satisfied_evidence_policy_queues_publication_once()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Current);
        await database.SeedCompletedResultAsync(requireReproduction: true, RepairClassification.Reproduced);

        var first = await database.RunCoordinatorAsync();
        var replay = await database.RunCoordinatorAsync();

        await using var verify = database.CreateContext();
        var publication = await verify.ProviderOperations.SingleAsync(x => x.Kind == ProviderOperationKind.PublishPullRequest);
        var attempt = await verify.RepairAttempts.SingleAsync();
        first.Should().Be(HealingRepairCoordinatorStatus.PublicationQueued);
        replay.Should().Be(HealingRepairCoordinatorStatus.Idle);
        attempt.Status.Should().Be(RepairAttemptStatus.Publishing);
        publication.IdempotencyKey.Should().StartWith($"publish:{attempt.Id:N}:");
        (await verify.PolicyEvaluations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Result_received_blocked_by_evidence_policy_stops_without_queueing_publication()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Current);
        await database.SeedCompletedResultAsync(requireReproduction: true, RepairClassification.InferredHighConfidence);

        var result = await database.RunCoordinatorAsync();

        await using var verify = database.CreateContext();
        var attempt = await verify.RepairAttempts.SingleAsync();
        var incident = await verify.HealingIncidents.SingleAsync();
        result.Should().Be(HealingRepairCoordinatorStatus.Idle);
        attempt.Status.Should().Be(RepairAttemptStatus.Stopped);
        attempt.OutcomeCode.Should().Be("evidence-policy-blocked");
        incident.Status.Should().Be(HealingIncidentStatus.NeedsHuman);
        incident.NeedsHumanReason.Should().Be(NeedsHumanReason.PolicyBlocked);
        (await verify.ProviderOperations.AnyAsync(x => x.Kind == ProviderOperationKind.PublishPullRequest)).Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_handler_revalidates_authority_and_blocks_revoked_application_before_provider_call()
    {
        await using var database = await CoordinatorDatabase.CreateAsync(WorkItemProjectionStatus.Pending);
        await using var dbContext = database.CreateContext();
        var provider = new RecordingRepairWorkProvider();
        var handler = new GitHubUpsertWorkItemOperationHandler(
            provider,
            dbContext,
            database.TimeProvider,
            new HealingRepairAuthorityService(dbContext, Options.Create(database.Options)));
        var request = new RepairWorkItemUpsertRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(database.Ids.ProviderId, "repository-1", "acme", "checkout"),
            database.Ids.IncidentId,
            database.Ids.EpisodeId,
            "Incident",
            "{}",
            new string('a', 64),
            "work-item:revoked");
        var operation = new ProviderOperation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = database.Ids.WorkspaceId,
            ApplicationId = database.Ids.ApplicationId,
            ProviderConnectionId = database.Ids.ProviderId,
            IncidentId = database.Ids.IncidentId,
            Kind = ProviderOperationKind.UpsertWorkItem,
            IdempotencyKey = request.IdempotencyKey,
            PayloadJson = JsonSerializer.Serialize(request),
            PayloadHash = new string('b', 64),
            Status = ProviderOperationStatus.Leased,
            CreatedAt = database.TimeProvider.GetUtcNow(),
            UpdatedAt = database.TimeProvider.GetUtcNow()
        };
        await dbContext.HealingConfigurations.ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.ApplicationKillSwitch, true));

        var outcome = await handler.ExecuteAsync(operation);

        outcome.Disposition.Should().Be(HealingOperationDisposition.DeadLettered);
        outcome.OutcomeCode.Should().Be("healing-authority-revoked");
        provider.UpsertCalls.Should().Be(0);
    }

    private sealed class CoordinatorDatabase : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T15:00:00Z");
        private static readonly string TargetRevision = new('a', 40);
        private readonly string _path;
        private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

        private CoordinatorDatabase(string path, CoordinatorIds ids)
        {
            _path = path;
            Ids = ids;
            TimeProvider = new FixedTimeProvider(Now);
            Options = new HealingOptions
            {
                RepairDispatchEnabled = true,
                Budgets = new HealingBudgetOptions
                {
                    MaxRepairAttempts = 2,
                    MaxConcurrentOperations = 4,
                    MaxInferenceUnits = 1_000,
                    MaxRepositoryRuns = 2,
                    TimeBudget = TimeSpan.FromMinutes(10)
                }
            };
        }

        public CoordinatorIds Ids { get; }
        public FixedTimeProvider TimeProvider { get; }
        public HealingOptions Options { get; }

        public static async Task<CoordinatorDatabase> CreateAsync(WorkItemProjectionStatus projectionStatus)
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-healing-coordinator-{Guid.NewGuid():N}.db");
            var ids = new CoordinatorIds(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var database = new CoordinatorDatabase(path, ids);
            await using var dbContext = database.CreateContext();
            await dbContext.Database.EnsureCreatedAsync();
            await database.SeedAsync(dbContext, projectionStatus);
            return database;
        }

        public HealingDbContext CreateContext() => new(new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={_path};Default Timeout=30;Pooling=False")
            .Options);

        public string UnprotectDispatchNonce(string protectedNonce) =>
            _dataProtectionProvider.CreateProtector("Elsa.Platform.Healing.DispatchNonce.v1")
                .Unprotect(protectedNonce[3..]);

        public async ValueTask<HealingRepairCoordinatorStatus> RunCoordinatorAsync()
        {
            await using var dbContext = CreateContext();
            var store = new HealingStore(dbContext);
            var targetInspector = new FixedTargetInspector(TargetRevision);
            var evidenceService = new HealingEvidenceService(
                store,
                new HealingEvidenceSource(dbContext),
                new DenyElevationAuthorizer(),
                TimeProvider);
            var orchestrationService = new RepairOrchestrationService(store, store, targetInspector, TimeProvider);
            var providerOperations = new ProviderOperationService(
                store,
                [],
                Options,
                $"test:{Guid.NewGuid():N}",
                TimeProvider);
            var coordinator = new HealingRepairCoordinator(
                dbContext,
                evidenceService,
                orchestrationService,
                targetInspector,
                providerOperations,
                _dataProtectionProvider,
                new HealingRepairAuthorityService(dbContext, Microsoft.Extensions.Options.Options.Create(Options)),
                Microsoft.Extensions.Options.Options.Create(Options),
                new HealingGitHubOptions
                {
                    PlatformBaseUrl = "https://platform.example.test",
                    WorkloadAudience = "elsa-platform-healing"
                },
                TimeProvider);
            return await coordinator.RunOnceAsync();
        }

        public async Task SeedAbandonedAttemptAsync(int attemptNumber)
        {
            await using var dbContext = CreateContext();
            var evidence = Evidence();
            dbContext.EvidenceBundles.Add(evidence);
            dbContext.RepairAttempts.Add(new RepairAttempt
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                IncidentId = Ids.IncidentId,
                EpisodeId = Ids.EpisodeId,
                BindingId = Ids.BindingId,
                AttemptNumber = attemptNumber,
                TargetRevision = TargetRevision,
                Status = RepairAttemptStatus.Running,
                EvidenceBundleId = evidence.Id,
                RepairClassification = RepairClassification.InsufficientConfidence,
                NonceHash = new string('c', 64),
                LeaseOwner = "abandoned-worker",
                LeaseToken = new string('d', 64),
                LeaseExpiresAt = Now.AddMinutes(-1),
                BudgetJson = "{}",
                UsageJson = "{}"
            });
            await dbContext.SaveChangesAsync();
        }

        public async Task SetApplicationAttemptLimitAsync(int attemptLimit)
        {
            await using var dbContext = CreateContext();
            await dbContext.HealingConfigurations.ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.DefaultAttemptLimit, attemptLimit));
        }

        public async Task AbandonLatestAttemptAsync()
        {
            await using var dbContext = CreateContext();
            var attempt = await dbContext.RepairAttempts.OrderByDescending(x => x.AttemptNumber).FirstAsync();
            attempt.Status = RepairAttemptStatus.Running;
            attempt.LeaseOwner = "abandoned-worker";
            attempt.LeaseToken = new string('e', 64);
            attempt.LeaseExpiresAt = Now.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        public async Task SeedCompletedResultAsync(bool requireReproduction, RepairClassification classification)
        {
            await using var dbContext = CreateContext();
            var policy = await dbContext.EvidencePolicies.SingleAsync();
            policy.RequireReproduction = requireReproduction;
            policy.AllowHighConfidenceInference = false;
            policy.MinimumInferenceConfidence = 0.9m;
            policy.MaximumTier = EvidenceTier.DefaultRedacted;
            var evidence = Evidence();
            var attempt = new RepairAttempt
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                IncidentId = Ids.IncidentId,
                EpisodeId = Ids.EpisodeId,
                BindingId = Ids.BindingId,
                AttemptNumber = 1,
                ProducingRevision = TargetRevision,
                TargetRevision = TargetRevision,
                Status = RepairAttemptStatus.ResultReceived,
                EvidenceBundleId = evidence.Id,
                RepairClassification = classification,
                NonceHash = new string('f', 64),
                BudgetJson = "{}",
                UsageJson = "{}",
                CompletedAt = Now
            };
            var patchDigest = $"sha256:{new string('1', 64)}";
            dbContext.EvidenceBundles.Add(evidence);
            dbContext.RepairAttempts.Add(attempt);
            dbContext.RepairResults.Add(new RepairResult
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                AttemptId = attempt.Id,
                IdempotencyKey = "result-1",
                WorkflowRunId = "run-1",
                WorkflowRunAttempt = 1,
                BaseRevision = TargetRevision,
                TargetRevision = TargetRevision,
                Classification = classification,
                Confidence = classification == RepairClassification.Reproduced ? 1m : 0.95m,
                UnifiedDiff = "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -1 +1 @@\n-old\n+new\n",
                PatchDigest = patchDigest,
                EnvelopeDigest = new string('2', 64),
                ChangedPathsJson = JsonSerializer.Serialize(new[] { new RepairChangedPathSuggestion("a.cs", "modified", null) }),
                ReproductionJson = JsonSerializer.Serialize(new RepairReproductionEvidence(
                    classification == RepairClassification.Reproduced,
                    classification == RepairClassification.Reproduced,
                    classification == RepairClassification.Reproduced ? "reproduced" : "not-reproduced",
                    "safe summary",
                    [])),
                RegressionJson = JsonSerializer.Serialize(new RepairRegressionEvidence(true, "regression added", ["a.cs"])),
                ValidationJson = JsonSerializer.Serialize(new[]
                {
                    new RepairValidationResult("test", "dotnet test", "passed", "all passed", TimeSpan.FromSeconds(1))
                }),
                RiskJson = JsonSerializer.Serialize(new
                {
                    CausalSummary = "root cause",
                    RiskSuggestions = Array.Empty<string>(),
                    RollbackSummary = "revert commit",
                    Usage = new RepairUsageSummary(10, 5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)),
                    Timing = new RepairTimingSummary(Now.AddMinutes(-1), Now)
                }),
                SubmittedAt = Now
            });
            await dbContext.SaveChangesAsync();
        }

        private async Task SeedAsync(HealingDbContext dbContext, WorkItemProjectionStatus projectionStatus)
        {
            var now = Now.AddHours(-1);
            dbContext.HealingWorkspaceConfigurations.Add(new HealingWorkspaceConfiguration
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ids.WorkspaceId,
                CreatedAt = now,
                UpdatedAt = now
            });
            var configuration = new HealingConfiguration
            {
                Id = Ids.ConfigurationId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                DiscoveryEnabled = true,
                RepairEnabled = true,
                SignalProfileVersion = HealingContractVersions.SignalProfile,
                DefaultAttemptLimit = 2,
                VerificationWindow = TimeSpan.FromMinutes(10),
                TimeBudget = TimeSpan.FromMinutes(10),
                ConcurrencyBudget = 2,
                InferenceBudget = 1_000,
                RepositoryRunBudget = 2,
                CreatedAt = now,
                UpdatedAt = now
            };
            configuration.Environments.Add(new HealingEnvironmentConfiguration
            {
                Id = Guid.NewGuid(),
                HealingConfigurationId = configuration.Id,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                EnvironmentId = Ids.EnvironmentId,
                RepairEnabled = true,
                ClassificationPolicyJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.HealingConfigurations.Add(configuration);
            dbContext.ProviderConnections.Add(new ProviderConnection
            {
                Id = Ids.ProviderId,
                WorkspaceId = Ids.WorkspaceId,
                Provider = "GitHub",
                InstallationId = "installation-1",
                RepositoryProviderId = "repository-1",
                RepositoryOwner = "acme",
                RepositoryName = "checkout",
                CredentialReference = "secret://github-app",
                Status = ProviderConnectionStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.PathPolicies.Add(new PathPolicy
            {
                Id = Ids.PathPolicyId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                Name = "path",
                PolicyVersion = "1",
                PolicyHash = new string('3', 64),
                AllowedRootsJson = "[]",
                ForbiddenRootsJson = "[]",
                MaxFiles = 10,
                MaxChangedLines = 500,
                MaxPatchBytes = 64_000,
                CreatedAt = now
            });
            dbContext.EvidencePolicies.Add(new EvidencePolicy
            {
                Id = Ids.EvidencePolicyId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                Name = "evidence",
                PolicyVersion = "1",
                PolicyHash = new string('4', 64),
                MaximumTier = EvidenceTier.DefaultRedacted,
                PermittedFieldsJson = "[]",
                CreatedAt = now
            });
            dbContext.MergePolicies.Add(new MergePolicy
            {
                Id = Ids.MergePolicyId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                Name = "merge",
                PolicyVersion = "1",
                PolicyHash = new string('5', 64),
                RequiredChecksJson = "[]",
                ForbiddenChangeCategoriesJson = "[]",
                CreatedAt = now
            });
            dbContext.SourceOwnershipBindings.Add(new SourceOwnershipBinding
            {
                Id = Ids.BindingId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                Name = "checkout",
                SelectorKind = SourceSelectorKind.Package,
                SelectorPattern = "Acme.Checkout",
                ProviderConnectionId = Ids.ProviderId,
                RepositoryProviderId = "repository-1",
                RepositoryOwner = "acme",
                RepositoryName = "checkout",
                TargetBranch = "main",
                WorkflowIdentity = ".github/workflows/heal.yml",
                WorkflowReference = "refs/tags/elsa-healing-v1",
                WorkflowRevision = new string('b', 40),
                PathPolicyId = Ids.PathPolicyId,
                EvidencePolicyId = Ids.EvidencePolicyId,
                MergePolicyId = Ids.MergePolicyId,
                Status = SourceOwnershipBindingStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.HealingIncidents.Add(new HealingIncident
            {
                Id = Ids.IncidentId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                FingerprintVersion = "1",
                Fingerprint = new string('6', 64),
                RepairRepositoryKey = "github:repository-1",
                Status = HealingIncidentStatus.ReadyForRepair,
                Severity = IncidentSeverity.Error,
                Classification = IncidentClassification.UnhandledRequest,
                SelectedBindingId = Ids.BindingId,
                FirstSeenAt = now,
                LastSeenAt = now,
                OccurrenceCount = 1
            });
            await dbContext.SaveChangesAsync();

            dbContext.IncidentEpisodes.Add(new IncidentEpisode
            {
                Id = Ids.EpisodeId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                IncidentId = Ids.IncidentId,
                OpenedAt = now,
                ProducingRevisionsJson = "[]",
                TargetRevision = TargetRevision,
                Outcome = IncidentEpisodeOutcome.Active
            });
            await dbContext.SaveChangesAsync();
            dbContext.EnvironmentImpacts.Add(new EnvironmentImpact
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                EpisodeId = Ids.EpisodeId,
                EnvironmentId = Ids.EnvironmentId,
                FirstSeenAt = now,
                LastSeenAt = now,
                OccurrenceCount = 1,
                ProducingRevisionsJson = "[]",
                VerificationStatus = VerificationOutcome.PendingDeployment,
                OccurrenceThreshold = 1,
                ClassificationPolicyVersion = "1",
                ClassificationPolicyHash = new string('7', 64)
            });
            dbContext.RepairWorkItemProjections.Add(new RepairWorkItemProjection
            {
                Id = Ids.ProjectionId,
                WorkspaceId = Ids.WorkspaceId,
                ApplicationId = Ids.ApplicationId,
                IncidentId = Ids.IncidentId,
                EpisodeId = Ids.EpisodeId,
                ProviderConnectionId = Ids.ProviderId,
                ProviderWorkItemId = projectionStatus == WorkItemProjectionStatus.Current ? "issue-1" : null,
                Number = projectionStatus == WorkItemProjectionStatus.Current ? 1 : null,
                Url = projectionStatus == WorkItemProjectionStatus.Current ? "https://github.com/acme/checkout/issues/1" : null,
                MachineSummaryHash = new string('8', 64),
                ProviderState = projectionStatus == WorkItemProjectionStatus.Current ? "open" : null,
                ProjectionStatus = projectionStatus,
                LastProjectedAt = projectionStatus == WorkItemProjectionStatus.Current ? now : null
            });
            await dbContext.SaveChangesAsync();
            await dbContext.HealingIncidents.Where(x => x.Id == Ids.IncidentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ActiveEpisodeId, Ids.EpisodeId)
                    .SetProperty(x => x.WorkItemProjectionId, Ids.ProjectionId));
        }

        private EvidenceBundle Evidence() => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Ids.WorkspaceId,
            ApplicationId = Ids.ApplicationId,
            IncidentId = Ids.IncidentId,
            Tier = EvidenceTier.DefaultRedacted,
            CanonicalJson = "{}",
            Digest = new string('9', 64),
            ProvenanceJson = "{}",
            OmissionsJson = "[]",
            SizeBytes = 2,
            CreatedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.AddMinutes(30)
        };

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_path))
                File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record CoordinatorIds(
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid ConfigurationId,
        Guid ProviderId,
        Guid BindingId,
        Guid IncidentId,
        Guid EpisodeId,
        Guid ProjectionId,
        Guid PathPolicyId)
    {
        public Guid EvidencePolicyId { get; } = Guid.NewGuid();
        public Guid MergePolicyId { get; } = Guid.NewGuid();
    }

    private sealed class FixedTargetInspector(string revision) : IRepairTargetInspector
    {
        public ValueTask<RepairTargetInspection> InspectAsync(
            RepairTargetInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RepairTargetInspection(RepairTargetState.Unresolved, revision));
    }

    private sealed class DenyElevationAuthorizer : IHealingEvidenceElevationAuthorizer
    {
        public ValueTask<EvidenceElevationAuthorization> AuthorizeAsync(
            EvidenceElevationAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EvidenceElevationAuthorization.Denied("not-authorized"));
    }

    private sealed class RecordingRepairWorkProvider : IRepairWorkProvider
    {
        public int UpsertCalls { get; private set; }

        public ValueTask<ProviderWorkItemReference> UpsertWorkItemAsync(
            RepairWorkItemUpsertRequest request,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return ValueTask.FromResult(new ProviderWorkItemReference(
                "issue-1", 1, new Uri("https://github.com/acme/checkout/issues/1"), "open", null));
        }

        public ValueTask<ProviderOperationReceipt> DispatchWorkflowAsync(
            RepairWorkflowDispatchRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
