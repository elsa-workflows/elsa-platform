using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Repairs;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Platform.Healing.Core.Tests.Repairs;

public sealed class RepairOrchestrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task DefaultEvidenceContainsOnlyTheBoundedRedactedTier()
    {
        var store = new RecordingEvidenceStore();
        var source = new StubEvidenceSource(new Dictionary<EvidenceField, string?>
        {
            [EvidenceField.ExceptionType] = "System.InvalidOperationException",
            [EvidenceField.OperationName] = "workflow.execute",
            [EvidenceField.NormalizedStack] = "at Acme.Activities.SendEmail.Execute()",
            [EvidenceField.SafeAttributes] = "Authorization=Bearer top-secret",
            [EvidenceField.TraceCorrelation] = "00-0123456789abcdef-0123456789abcdef-01"
        });
        var service = new HealingEvidenceService(store, source, new DenyElevationAuthorizer(), new FixedTimeProvider(Now));
        var request = EvidenceBundleRequest.CreateDefault(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await service.CreateDefaultAsync(request);

        result.Succeeded.Should().BeTrue();
        result.Bundle!.Tier.Should().Be(EvidenceTier.DefaultRedacted);
        result.Bundle.CanonicalJson.Should().Contain("System.InvalidOperationException");
        result.Bundle.CanonicalJson.Should().Contain("workflow.execute");
        result.Bundle.CanonicalJson.Should().Contain("Acme.Activities.SendEmail.Execute");
        result.Bundle.CanonicalJson.Should().NotContain("Authorization");
        result.Bundle.CanonicalJson.Should().NotContain("top-secret");
        result.Bundle.CanonicalJson.Should().NotContain("0123456789abcdef");
        result.Bundle.OmissionsJson.Should().Contain("safeAttributes");
        result.Bundle.OmissionsJson.Should().Contain("traceCorrelation");
        result.Bundle.SizeBytes.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(HealingEvidenceService.MaximumBundleBytes);
        result.Bundle.Digest.Should().MatchRegex("^[a-f0-9]{64}$");
        store.Bundles.Should().ContainSingle();
    }

    [Fact]
    public async Task EvidenceElevationIsDeniedAndAuditedWithoutChangingTheDefaultBundle()
    {
        var store = new RecordingEvidenceStore();
        var source = new StubEvidenceSource(new Dictionary<EvidenceField, string?>
        {
            [EvidenceField.ExceptionType] = "System.InvalidOperationException",
            [EvidenceField.TraceCorrelation] = "trace-123"
        });
        var service = new HealingEvidenceService(store, source, new DenyElevationAuthorizer(), new FixedTimeProvider(Now));
        var scope = EvidenceBundleRequest.CreateDefault(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var baseline = (await service.CreateDefaultAsync(scope)).Bundle!;
        var originalJson = baseline.CanonicalJson;
        var request = new EvidenceElevationRequest(
            scope.WorkspaceId,
            scope.ApplicationId,
            scope.IncidentId,
            baseline.Id,
            Guid.NewGuid(),
            "operator-1",
            "reproduce-concurrency-failure",
            new HashSet<EvidenceField> { EvidenceField.TraceCorrelation });

        var result = await service.ElevateAsync(request);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("not-authorized");
        store.Bundles.Should().ContainSingle();
        baseline.CanonicalJson.Should().Be(originalJson);
        store.Decisions.Should().ContainSingle().Which.Should().Match<EvidenceAccessDecision>(x =>
            !x.Authorized && x.ReleasedBundleId == null && x.RequestedTier == EvidenceTier.Elevated);
    }

    [Fact]
    public async Task AuthorizedElevationCreatesANewExpiringTierAndAuditsTheRelease()
    {
        var store = new RecordingEvidenceStore();
        var source = new StubEvidenceSource(new Dictionary<EvidenceField, string?>
        {
            [EvidenceField.ExceptionType] = "System.InvalidOperationException",
            [EvidenceField.TraceCorrelation] = "trace-123"
        });
        var service = new HealingEvidenceService(store, source, new AllowElevationAuthorizer(), new FixedTimeProvider(Now));
        var scope = EvidenceBundleRequest.CreateDefault(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var baseline = (await service.CreateDefaultAsync(scope)).Bundle!;
        var originalDigest = baseline.Digest;
        var targetAttemptId = Guid.NewGuid();

        var result = await service.ElevateAsync(new EvidenceElevationRequest(
            scope.WorkspaceId,
            scope.ApplicationId,
            scope.IncidentId,
            baseline.Id,
            targetAttemptId,
            "operator-1",
            "reproduce-concurrency-failure",
            new HashSet<EvidenceField> { EvidenceField.TraceCorrelation }));

        result.Succeeded.Should().BeTrue();
        result.Bundle!.Id.Should().NotBe(baseline.Id);
        result.Bundle.Tier.Should().Be(EvidenceTier.Elevated);
        result.Bundle.CanonicalJson.Should().Contain("trace-123");
        result.Bundle.ExpiresAt.Should().Be(Now.AddHours(1));
        baseline.Tier.Should().Be(EvidenceTier.DefaultRedacted);
        baseline.Digest.Should().Be(originalDigest);
        store.ElevatedTarget.Should().Be((targetAttemptId, baseline.Id));
        store.Bundles.Should().HaveCount(2);
        store.Decisions.Should().ContainSingle().Which.Should().Match<EvidenceAccessDecision>(x =>
            x.Authorized && x.ReleasedBundleId == result.Bundle.Id && x.ApprovedBy == "security-reviewer");
    }

    [Fact]
    public async Task EvidenceElevationRejectsFieldsOutsideTheExplicitElevationAllowlist()
    {
        var store = new RecordingEvidenceStore();
        var source = new StubEvidenceSource(new Dictionary<EvidenceField, string?>
        {
            [EvidenceField.ExceptionType] = "System.InvalidOperationException"
        });
        var service = new HealingEvidenceService(store, source, new AllowElevationAuthorizer(), new FixedTimeProvider(Now));
        var scope = EvidenceBundleRequest.CreateDefault(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var baseline = (await service.CreateDefaultAsync(scope)).Bundle!;

        var result = await service.ElevateAsync(new EvidenceElevationRequest(
            scope.WorkspaceId,
            scope.ApplicationId,
            scope.IncidentId,
            baseline.Id,
            Guid.NewGuid(),
            "operator-1",
            "request-raw-body",
            new HashSet<EvidenceField> { EvidenceField.ExceptionType }));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("field-not-elevatable");
        store.Bundles.Should().ContainSingle();
        store.Decisions.Should().ContainSingle().Which.Authorized.Should().BeFalse();
    }

    [Fact]
    public async Task AttemptCreationAtomicallyCapsAnEpisodeAndTargetAtTwoAttempts()
    {
        var (service, store, evidence, request) = CreateOrchestration(RepairTargetState.Unresolved);

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => service.CreateAttemptAsync(request).AsTask()));

        results.Count(x => x.Outcome == RepairAttemptCreationOutcome.Created).Should().Be(2);
        results.Count(x => x.Outcome == RepairAttemptCreationOutcome.AttemptLimitReached).Should().Be(10);
        store.Attempts.Select(x => x.AttemptNumber).Should().BeEquivalentTo([1, 2]);
        store.Attempts.Should().OnlyContain(x => x.EvidenceBundleId == evidence.Id);
        results.Where(x => x.Succeeded).Should().AllSatisfy(x =>
        {
            x.OneTimeNonce.Should().NotBeNull().And.HaveLength(43);
            x.Attempt!.NonceHash.Should().NotBe(x.OneTimeNonce).And.HaveLength(64);
        });
    }

    [Theory]
    [InlineData(RepairTargetState.AlreadyFixed, RepairAttemptCreationOutcome.AlreadyFixed, "already-fixed")]
    [InlineData(RepairTargetState.Unknown, RepairAttemptCreationOutcome.TargetStateUnknown, "target-state-unknown")]
    public async Task AttemptCreationFailsClosedWhenTheTargetIsFixedOrCannotBeVerified(
        RepairTargetState state,
        RepairAttemptCreationOutcome expectedOutcome,
        string reasonCode)
    {
        var (service, store, _, request) = CreateOrchestration(state);

        var result = await service.CreateAttemptAsync(request);

        result.Outcome.Should().Be(expectedOutcome);
        result.ReasonCode.Should().Be(reasonCode);
        result.Attempt.Should().BeNull();
        result.OneTimeNonce.Should().BeNull();
        store.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task AttemptLeaseUsesAnOpaqueTokenAndRejectsWrongOrExpiredHeartbeats()
    {
        var timeProvider = new MutableTimeProvider(Now);
        var (service, store, _, request) = CreateOrchestration(RepairTargetState.Unresolved, timeProvider);
        var attempt = (await service.CreateAttemptAsync(request)).Attempt!;

        var lease = await service.AcquireLeaseAsync(request.WorkspaceId, attempt.Id, "runner-1", TimeSpan.FromMinutes(5));
        var wrongHeartbeat = await service.HeartbeatAsync(
            request.WorkspaceId,
            attempt.Id,
            "wrong-token",
            TimeSpan.FromMinutes(5));
        var heartbeat = await service.HeartbeatAsync(
            request.WorkspaceId,
            attempt.Id,
            lease.LeaseToken!,
            TimeSpan.FromMinutes(5));
        timeProvider.Advance(TimeSpan.FromMinutes(6));
        var expiredHeartbeat = await service.HeartbeatAsync(
            request.WorkspaceId,
            attempt.Id,
            lease.LeaseToken!,
            TimeSpan.FromMinutes(5));

        lease.Succeeded.Should().BeTrue();
        lease.LeaseToken.Should().NotBeNullOrWhiteSpace();
        store.Attempts.Single().LeaseToken.Should().NotBe(lease.LeaseToken);
        store.Attempts.Single().LeaseToken.Should().HaveLength(64);
        wrongHeartbeat.ReasonCode.Should().Be("lease-lost");
        heartbeat.Succeeded.Should().BeTrue();
        heartbeat.ExpiresAt.Should().Be(Now.AddMinutes(5));
        expiredHeartbeat.ReasonCode.Should().Be("lease-lost");
    }

    [Theory]
    [InlineData(true, ReproductionOutcome.Reproduced, "1.0", null, RepairClassification.Reproduced, true, true)]
    [InlineData(true, ReproductionOutcome.NotReproduced, "0.95", "production-only-timing", RepairClassification.InferredHighConfidence, true, false)]
    [InlineData(true, ReproductionOutcome.NotAttempted, "0.90", "dependency-unavailable", RepairClassification.InferredHighConfidence, false, false)]
    [InlineData(true, ReproductionOutcome.NotReproduced, "0.79", "production-only-timing", RepairClassification.InsufficientConfidence, true, false)]
    [InlineData(false, ReproductionOutcome.Reproduced, "1.0", null, RepairClassification.RevisionUnverified, true, true)]
    public async Task ReproductionClassificationRecordsExplicitAttemptAndReproducedMetadata(
        bool producingRevisionVerified,
        ReproductionOutcome outcome,
        string confidenceText,
        string? reasonCode,
        RepairClassification expected,
        bool expectedAttempted,
        bool expectedReproduced)
    {
        var (service, store, _, request) = CreateOrchestration(RepairTargetState.Unresolved);
        request = request with { ProducingRevisionVerified = producingRevisionVerified };
        var attempt = (await service.CreateAttemptAsync(request)).Attempt!;
        var lease = await service.AcquireLeaseAsync(request.WorkspaceId, attempt.Id, "runner-1", TimeSpan.FromMinutes(5));

        var result = await service.RecordReproductionAsync(new RepairReproductionSubmission(
            request.WorkspaceId,
            attempt.Id,
            lease.LeaseToken!,
            outcome,
            decimal.Parse(confidenceText, System.Globalization.CultureInfo.InvariantCulture),
            reasonCode,
            new string('a', 64)));

        result.Succeeded.Should().BeTrue();
        result.Classification.Should().Be(expected);
        result.ReproductionAttempted.Should().Be(expectedAttempted);
        result.Reproduced.Should().Be(expectedReproduced);
        store.ReproductionJson[attempt.Id].Should().Contain($"\"reproductionAttempted\":{expectedAttempted.ToString().ToLowerInvariant()}");
        store.ReproductionJson[attempt.Id].Should().Contain($"\"reproduced\":{expectedReproduced.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public async Task ReproductionSubmissionRequiresAnExplicitOutcomeAndReasonWhenNotReproduced()
    {
        var (service, _, _, request) = CreateOrchestration(RepairTargetState.Unresolved);
        var attempt = (await service.CreateAttemptAsync(request)).Attempt!;
        var lease = await service.AcquireLeaseAsync(request.WorkspaceId, attempt.Id, "runner-1", TimeSpan.FromMinutes(5));
        var submission = new RepairReproductionSubmission(
            request.WorkspaceId,
            attempt.Id,
            lease.LeaseToken!,
            ReproductionOutcome.NotAttempted,
            0.95m,
            null,
            new string('a', 64));

        var act = () => service.RecordReproductionAsync(submission).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*explicit bounded reason*");
    }

    private static (RepairOrchestrationService Service, InMemoryRepairStore Store, EvidenceBundle Evidence, CreateRepairAttemptRequest Request)
        CreateOrchestration(RepairTargetState targetState, TimeProvider? timeProvider = null)
    {
        var now = timeProvider?.GetUtcNow() ?? Now;
        var evidenceStore = new RecordingEvidenceStore();
        var evidence = new EvidenceBundle
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            Tier = EvidenceTier.DefaultRedacted,
            CanonicalJson = "{}",
            Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("{}"))),
            SizeBytes = 2,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };
        evidenceStore.Bundles.Add(evidence);
        var store = new InMemoryRepairStore();
        var targetRevision = new string('c', 40);
        var service = new RepairOrchestrationService(
            store,
            evidenceStore,
            new StubTargetInspector(new RepairTargetInspection(targetState, targetState == RepairTargetState.Unknown ? "" : targetRevision)),
            timeProvider ?? new FixedTimeProvider(Now));
        var request = new CreateRepairAttemptRequest(
            evidence.WorkspaceId,
            evidence.ApplicationId,
            evidence.IncidentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            evidence.Id,
            targetRevision,
            new string('d', 40),
            true,
            "{\"maxTokens\":1000}");
        return (service, store, evidence, request);
    }

    private sealed class RecordingEvidenceStore : IHealingEvidenceStore
    {
        public List<EvidenceBundle> Bundles { get; } = [];
        public List<EvidenceAccessDecision> Decisions { get; } = [];
        public (Guid AttemptId, Guid BaseBundleId)? ElevatedTarget { get; private set; }

        public ValueTask<bool> TryAppendBundleAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default)
        {
            Bundles.Add(bundle);
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> TryAppendElevatedBundleAsync(
            EvidenceBundle bundle,
            EvidenceAccessDecision decision,
            Guid targetAttemptId,
            Guid expectedBaseBundleId,
            CancellationToken cancellationToken = default)
        {
            Bundles.Add(bundle);
            Decisions.Add(decision);
            ElevatedTarget = (targetAttemptId, expectedBaseBundleId);
            return ValueTask.FromResult(true);
        }

        public ValueTask<EvidenceBundle?> FindBundleAsync(Guid workspaceId, Guid bundleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Bundles.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == bundleId));

        public ValueTask AppendAccessDecisionAsync(EvidenceAccessDecision decision, CancellationToken cancellationToken = default)
        {
            Decisions.Add(decision);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubEvidenceSource(IReadOnlyDictionary<EvidenceField, string?> values) : IHealingEvidenceSource
    {
        public ValueTask<EvidenceSourceSnapshot> ReadAsync(
            EvidenceSourceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EvidenceSourceSnapshot(values, new Dictionary<EvidenceField, string>()));
    }

    private sealed class StubTargetInspector(RepairTargetInspection inspection) : IRepairTargetInspector
    {
        public ValueTask<RepairTargetInspection> InspectAsync(
            RepairTargetInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(inspection);
    }

    private sealed class InMemoryRepairStore : IRepairOrchestrationStore
    {
        private readonly Lock _lock = new();
        public List<RepairAttempt> Attempts { get; } = [];
        public Dictionary<Guid, string> ReproductionJson { get; } = [];

        public ValueTask<RepairAttemptStoreCreateResult> TryCreateAttemptAsync(
            RepairAttempt attempt,
            int maximumAttempts,
            int maximumConcurrentAttempts,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var activeApplicationAttempts = Attempts.Count(x =>
                    x.WorkspaceId == attempt.WorkspaceId &&
                    x.ApplicationId == attempt.ApplicationId &&
                    x.Status is RepairAttemptStatus.Queued or RepairAttemptStatus.Dispatched or
                        RepairAttemptStatus.Running or RepairAttemptStatus.ProposalReady or
                        RepairAttemptStatus.ResultReceived or RepairAttemptStatus.Publishing);
                if (activeApplicationAttempts >= maximumConcurrentAttempts)
                    return ValueTask.FromResult(new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.ConcurrencyLimitReached, null));
                var matching = Attempts.Where(x =>
                    x.WorkspaceId == attempt.WorkspaceId
                    && x.EpisodeId == attempt.EpisodeId
                    && string.Equals(x.TargetRevision, attempt.TargetRevision, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matching.Count >= maximumAttempts)
                    return ValueTask.FromResult(new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.AttemptLimitReached, null));

                attempt.AttemptNumber = matching.Count + 1;
                Attempts.Add(attempt);
                return ValueTask.FromResult(new RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome.Created, attempt));
            }
        }

        public ValueTask<RepairAttempt?> FindAttemptAsync(
            Guid workspaceId,
            Guid attemptId,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
                return ValueTask.FromResult(Attempts.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == attemptId));
        }

        public ValueTask<bool> TryAcquireLeaseAsync(
            Guid workspaceId,
            Guid attemptId,
            string leaseOwner,
            string leaseTokenHash,
            DateTimeOffset now,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var attempt = Attempts.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == attemptId);
                if (attempt is null || attempt.Status is not (RepairAttemptStatus.Queued or RepairAttemptStatus.Dispatched)
                    || attempt.LeaseExpiresAt > now)
                    return ValueTask.FromResult(false);
                attempt.Status = RepairAttemptStatus.Running;
                attempt.LeaseOwner = leaseOwner;
                attempt.LeaseToken = leaseTokenHash;
                attempt.LeaseExpiresAt = expiresAt;
                attempt.StartedAt ??= now;
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<bool> TryHeartbeatLeaseAsync(
            Guid workspaceId,
            Guid attemptId,
            string leaseTokenHash,
            DateTimeOffset now,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var attempt = Attempts.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == attemptId);
                if (attempt is null || attempt.Status != RepairAttemptStatus.Running || attempt.LeaseExpiresAt <= now
                    || !FixedTimeEquals(attempt.LeaseToken, leaseTokenHash))
                    return ValueTask.FromResult(false);
                attempt.LeaseExpiresAt = expiresAt;
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<bool> TryRecordReproductionAsync(
            Guid workspaceId,
            Guid attemptId,
            string leaseTokenHash,
            RepairClassification classification,
            string reproductionJson,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var attempt = Attempts.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == attemptId);
                if (attempt is null || attempt.Status != RepairAttemptStatus.Running || attempt.LeaseExpiresAt <= now
                    || !FixedTimeEquals(attempt.LeaseToken, leaseTokenHash))
                    return ValueTask.FromResult(false);
                attempt.RepairClassification = classification;
                attempt.Status = RepairAttemptStatus.ResultReceived;
                ReproductionJson[attempt.Id] = reproductionJson;
                return ValueTask.FromResult(true);
            }
        }

        private static bool FixedTimeEquals(string? left, string right) =>
            left is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    }

    private sealed class DenyElevationAuthorizer : IHealingEvidenceElevationAuthorizer
    {
        public ValueTask<EvidenceElevationAuthorization> AuthorizeAsync(
            EvidenceElevationAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EvidenceElevationAuthorization.Denied("not-authorized"));
    }

    private sealed class AllowElevationAuthorizer : IHealingEvidenceElevationAuthorizer
    {
        public ValueTask<EvidenceElevationAuthorization> AuthorizeAsync(
            EvidenceElevationAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EvidenceElevationAuthorization.Approved("security-reviewer"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
