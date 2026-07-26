using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Incidents;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Core.Verification;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.OpenTelemetry;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class HealingEndToEndTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
    private static readonly LifecycleIds Ids = LifecycleIds.Create();
    private static readonly string BaseRevision = new('c', 40);
    private static readonly string HeadRevision = new('d', 40);
    private static readonly string MergedRevision = new('e', 40);

    [Fact]
    public async Task Otlp_occurrences_group_then_fake_provider_pr_merge_and_positive_deployment_verification_close_the_incident()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var store = new HealingStore(db);
        var time = new MutableTimeProvider(Now);
        var audit = new HealingAuditService(store, time);
        var options = EnabledOptions();
        var verificationStore = new HealingVerificationStore(db, store);
        var verification = new HealingVerificationService(verificationStore, time, audit);
        var appender = new PlatformHealingSignalInboxAppender(store);

        await using var telemetryServices = TelemetryServices(appender, verification).BuildServiceProvider();
        var contributor = new HealingOpenTelemetryIngestionContributor(
            telemetryServices.GetRequiredService<IServiceScopeFactory>(), time);
        await contributor.ContributeAsync(ExceptionBatch("occurrence-1", Now.AddMinutes(-2)), TrustedContext());
        await contributor.ContributeAsync(ExceptionBatch("occurrence-2", Now.AddMinutes(-1)), TrustedContext());

        var ownership = new SourceOwnershipService(store, audit, time);
        var worker = new HealingSignalInboxWorker(
            store, store, new HealingSignalNormalizer(), new HealingSignalClassifier(),
            new ComponentAttributionService(store, ownership), new HealingFingerprintService(),
            new HealingIncidentService(store), audit, new HealingKillSwitch(options), Options.Create(options), time);
        (await worker.RunOnceAsync("lifecycle-1")).Status.Should().Be(HealingInboxWorkerStatus.Projected);
        (await worker.RunOnceAsync("lifecycle-2")).Status.Should().Be(HealingInboxWorkerStatus.Projected);

        db.ChangeTracker.Clear();
        var incident = await db.HealingIncidents.SingleAsync();
        var episode = await db.IncidentEpisodes.SingleAsync();
        var projection = await db.RepairWorkItemProjections.SingleAsync();
        incident.OccurrenceCount.Should().Be(2);
        (await db.IncidentOccurrences.CountAsync()).Should().Be(2);
        incident.Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
        projection.ProjectionStatus.Should().Be(WorkItemProjectionStatus.Pending);

        var fakeProvider = new FakeRepairProvider();
        var authority = new HealingRepairAuthorityService(db, Options.Create(options));
        var workItemRequest = new RepairWorkItemUpsertRequest(
            HealingContractVersions.ProviderProtocol, Repository(), incident.Id, episode.Id,
            "System.InvalidOperationException in orders.load", "{}", incident.Fingerprint, "work-item:lifecycle");
        var workItemOperation = ProviderOperation(
            ProviderOperationKind.UpsertWorkItem, "work-item:lifecycle", workItemRequest, incident.Id);
        var projected = await new GitHubUpsertWorkItemOperationHandler(fakeProvider, db, time, authority)
            .ExecuteAsync(workItemOperation);
        projected.OutcomeCode.Should().Be("work-item-projected");
        fakeProvider.WorkItemCalls.Should().Be(1);

        var evidence = new EvidenceBundle
        {
            Id = Guid.NewGuid(), WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId,
            IncidentId = incident.Id, Tier = EvidenceTier.DefaultRedacted, CanonicalJson = "{}", Digest = Hex('9'),
            ProvenanceJson = "{}", OmissionsJson = "[]", SizeBytes = 2, CreatedAt = Now, ExpiresAt = Now.AddHours(1)
        };
        var attempt = new RepairAttempt
        {
            Id = Guid.NewGuid(), WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId,
            IncidentId = incident.Id, EpisodeId = episode.Id, BindingId = Ids.BindingId, AttemptNumber = 1,
            ProducingRevision = BaseRevision, TargetRevision = BaseRevision, Status = RepairAttemptStatus.Publishing,
            EvidenceBundleId = evidence.Id, RepairClassification = RepairClassification.Reproduced,
            NonceHash = Hex('8'), BudgetJson = "{}", UsageJson = "{}"
        };
        db.EvidenceBundles.Add(evidence);
        db.RepairAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var repairResult = RepairResult(attempt.Id);
        var publication = new RepairPublicationRequest(
            HealingContractVersions.ProviderProtocol, Repository(), incident.Id, episode.Id, attempt.Id,
            "main", BaseRevision, repairResult,
            new(HealingContractVersions.PolicyProtocol, "1", Hex('3'), Hex('4'), PolicyDecisions.AllowPublication,
                [new("path", PolicyGateState.Pass, "allowed")], Now), "publish:lifecycle");
        var published = await new GitHubPublishPullRequestOperationHandler(
                new FakePatchPublisher(), db, authority)
            .ExecuteAsync(ProviderOperation(
                ProviderOperationKind.PublishPullRequest, publication.IdempotencyKey, publication, incident.Id, attempt.Id));
        published.OutcomeCode.Should().Be("repair-pull-request-published");

        db.ChangeTracker.Clear();
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Open);
        pullRequest.HeadRevision.Should().Be(HeadRevision);
        var delivery = new ProviderWebhookDelivery
        {
            Id = Guid.NewGuid(), WorkspaceId = Ids.WorkspaceId, ProviderDeliveryId = "merge-delivery-1",
            InstallationId = "42", RepositoryProviderId = "987", Event = "pull_request", Action = "closed",
            BodyDigest = Hex('7'), ReceivedAt = Now, Status = ProviderWebhookDeliveryStatus.Pending
        };
        db.ProviderWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var connection = await db.ProviderConnections.SingleAsync();
        var mergeBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            action = "closed",
            repository = new { id = 987 },
            pull_request = new
            {
                number = 12, draft = false, merged = true, merged_at = Now,
                merge_commit_sha = MergedRevision,
                head = new { @ref = $"elsa-healing/{attempt.Id:N}", sha = HeadRevision },
                @base = new { sha = BaseRevision }
            }
        }));
        var mergeOutcome = await new PlatformHealingGitHubWebhookProcessor(db, new GitHubWebhookProcessor(), time)
            .ProcessAsync(connection, delivery.ProviderDeliveryId, "pull_request", mergeBody);
        mergeOutcome.Should().Be("pull-request-merged");

        time.Set(Now.AddMinutes(1));
        var deployment = new DeploymentObservationService(verificationStore, verification, audit, time);
        await deployment.AppendAsync(new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol, Ids.WorkspaceId, Ids.ApplicationId, Ids.EnvironmentId,
            MergedRevision, time.GetUtcNow(), DeploymentObservationSources.PlatformDeployment,
            "deployment-1", "platform-deployment:test", Sha('6'), "deployment:lifecycle"));

        time.Set(Now.AddMinutes(2));
        await contributor.ContributeAsync(PositiveOperationBatch(time.GetUtcNow()), TrustedContext());
        db.ChangeTracker.Clear();
        var pending = await db.VerificationResults.SingleAsync();
        pending.Outcome.Should().Be(VerificationOutcome.DeployedUnverified);
        pending.RelevantOperationSuccessCount.Should().Be(1);

        time.Set(Now.AddMinutes(7));
        var due = await verificationStore.GetScopeAsync(
            Ids.WorkspaceId, episode.Id, Ids.EnvironmentId, MergedRevision);
        (await verification.EvaluateDueAsync(due!, time.GetUtcNow())).Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.VerificationResults.SingleAsync()).Outcome.Should().Be(VerificationOutcome.Healed);
        (await db.EnvironmentImpacts.SingleAsync()).VerificationStatus.Should().Be(VerificationOutcome.Healed);
        (await db.HealingIncidents.SingleAsync()).Status.Should().Be(HealingIncidentStatus.Healed);
        var closedEpisode = await db.IncidentEpisodes.SingleAsync();
        closedEpisode.Outcome.Should().Be(IncidentEpisodeOutcome.Healed);
        closedEpisode.ClosedAt.Should().Be(time.GetUtcNow());
    }

    private static async Task SeedAsync(HealingDbContext db)
    {
        var configuration = new HealingConfiguration
        {
            Id = Ids.ConfigurationId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId,
            DiscoveryEnabled = true, RepairEnabled = true, SignalProfileVersion = HealingContractVersions.SignalProfile,
            DefaultAttemptLimit = 2, VerificationWindow = TimeSpan.FromMinutes(5), TimeBudget = TimeSpan.FromMinutes(10),
            ConcurrencyBudget = 2, InferenceBudget = 1_000, RepositoryRunBudget = 2,
            ClassificationPolicyJson = "{\"version\":\"1\",\"thresholds\":{\"unhandled_request\":2},\"debounceSeconds\":0}",
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        };
        configuration.Environments.Add(new HealingEnvironmentConfiguration
        {
            Id = Guid.NewGuid(), HealingConfigurationId = configuration.Id, WorkspaceId = Ids.WorkspaceId,
            ApplicationId = Ids.ApplicationId, EnvironmentId = Ids.EnvironmentId, DiscoveryEnabled = true,
            RepairEnabled = true, OccurrenceThreshold = 2, DebounceWindow = TimeSpan.Zero,
            ClassificationPolicyJson = "{}", CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        });
        db.HealingWorkspaceConfigurations.Add(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(), WorkspaceId = Ids.WorkspaceId, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        });
        db.HealingConfigurations.Add(configuration);
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = Ids.ProviderId, WorkspaceId = Ids.WorkspaceId, Provider = "GitHub", InstallationId = "42",
            RepositoryProviderId = "987", RepositoryOwner = "acme", RepositoryName = "orders",
            CredentialReference = "secret://github-app", Status = ProviderConnectionStatus.Active,
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        });
        db.PathPolicies.Add(new PathPolicy
        {
            Id = Ids.PathPolicyId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId, Name = "path",
            PolicyVersion = "1", PolicyHash = Hex('3'), AllowedRootsJson = "[\"src\"]", ForbiddenRootsJson = "[]",
            MaxFiles = 5, MaxChangedLines = 100, MaxPatchBytes = 10_000, CreatedAt = Now.AddHours(-1)
        });
        db.EvidencePolicies.Add(new EvidencePolicy
        {
            Id = Ids.EvidencePolicyId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId, Name = "evidence",
            PolicyVersion = "1", PolicyHash = Hex('4'), MaximumTier = EvidenceTier.DefaultRedacted,
            PermittedFieldsJson = "[]", CreatedAt = Now.AddHours(-1)
        });
        db.MergePolicies.Add(new MergePolicy
        {
            Id = Ids.MergePolicyId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId, Name = "merge",
            PolicyVersion = "1", PolicyHash = Hex('5'), RequiredChecksJson = "[]",
            ForbiddenChangeCategoriesJson = "[]", CreatedAt = Now.AddHours(-1)
        });
        db.SourceOwnershipBindings.Add(new SourceOwnershipBinding
        {
            Id = Ids.BindingId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId, Name = "orders",
            SelectorKind = SourceSelectorKind.ComponentKey, SelectorPattern = "component:acme-orders",
            ProviderConnectionId = Ids.ProviderId, RepositoryProviderId = "987", RepositoryOwner = "acme",
            RepositoryName = "orders", TargetBranch = "main", WorkflowIdentity = ".github/workflows/heal.yml",
            WorkflowReference = "refs/heads/main", WorkflowRevision = new string('b', 40),
            PathPolicyId = Ids.PathPolicyId, EvidencePolicyId = Ids.EvidencePolicyId, MergePolicyId = Ids.MergePolicyId,
            Status = SourceOwnershipBindingStatus.Active, ApprovedBy = "owner", ApprovedAt = Now.AddHours(-1),
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        });
        var manifest = new ComponentManifest
        {
            Id = Ids.ManifestId, WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId,
            RevisionId = Ids.RevisionId, SchemaVersion = "1.0", SourceRevision = BaseRevision,
            ManifestDigest = Sha('a'), CanonicalJson = "{}", TrustState = ComponentManifestTrustState.Verified,
            VerifiedBy = "build-attestor", VerifiedAt = Now.AddHours(-1),
            VerificationMethod = "platform-managed-build-attestation", CreatedAt = Now.AddHours(-1)
        };
        manifest.Entries.Add(new Elsa.Platform.Healing.Core.ComponentManifestEntry
        {
            Id = Ids.ComponentId, ManifestId = manifest.Id, WorkspaceId = Ids.WorkspaceId,
            ApplicationId = Ids.ApplicationId, ComponentKey = "component:acme-orders",
            Kind = ComponentKind.Application, KindName = "application", Name = "Acme.Orders",
            AssemblyName = "Acme.Orders", ContentHash = Sha('b'), IsDirectDependency = true
        });
        db.ComponentManifests.Add(manifest);
        await db.SaveChangesAsync();
    }

    private static IServiceCollection TelemetryServices(
        IHealingSignalInboxAppender appender,
        HealingVerificationService verification) => new ServiceCollection()
        .AddSingleton(appender)
        .AddSingleton<IHealingTelemetryScopeResolver>(new StaticScopeResolver(
            new HealingTelemetryScope(Ids.WorkspaceId, Ids.ApplicationId, Ids.EnvironmentId)))
        .AddSingleton(verification);

    private static OpenTelemetryBatch ExceptionBatch(string occurrenceId, DateTimeOffset occurredAt)
    {
        var resource = Resource();
        var attributes = CommonAttributes();
        attributes[HealingSignalAttributes.OccurrenceId] = occurrenceId;
        attributes[HealingSignalAttributes.FailureClass] = HealingFailureClasses.UnhandledRequest;
        attributes[HealingSignalAttributes.RetryState] = HealingRetryStates.None;
        attributes["exception.type"] = "System.InvalidOperationException";
        attributes["exception.message"] = "redacted failure";
        attributes["exception.stacktrace"] = "at Acme.Orders.OrderService.Load()";
        var log = new OtlpLogRecord(occurrenceId, resource.Id, occurredAt, "Error", 17, "redacted failure",
            $"trace-{occurrenceId}", $"span-{occurrenceId}", attributes);
        return new([resource], [], [], [], [], [log]);
    }

    private static OpenTelemetryBatch PositiveOperationBatch(DateTimeOffset observedAt)
    {
        var resource = Resource();
        var attributes = CommonAttributes();
        attributes[HealingSignalAttributes.SourceRevision] = MergedRevision;
        attributes[HealingSignalAttributes.VerificationAffectedOperation] = "true";
        var span = new TelemetrySpan(
            "orders.load", "trace-positive", "span-positive", null, resource.Id, "orders.load", "Internal",
            observedAt.AddSeconds(-1), observedAt, SpanStatus.Ok, null, attributes, [], []);
        return new([resource], [], [span], [], [], []);
    }

    private static Dictionary<string, string?> CommonAttributes() => new(StringComparer.Ordinal)
    {
        [HealingSignalAttributes.ProfileVersion] = HealingContractVersions.SignalProfile,
        [HealingSignalAttributes.ApplicationId] = Ids.ApplicationId.ToString("D"),
        [HealingSignalAttributes.EnvironmentId] = Ids.EnvironmentId.ToString("D"),
        [HealingSignalAttributes.RevisionId] = Ids.RevisionId.ToString("D"),
        [HealingSignalAttributes.SourceRevision] = BaseRevision,
        [HealingSignalAttributes.ComponentManifestDigest] = Sha('a'),
        [HealingSignalAttributes.ComponentKey] = "component:acme-orders",
        [HealingSignalAttributes.OperationName] = "orders.load"
    };

    private static TelemetryResource Resource() => new(
        "orders-resource", "orders-api", "instance-1", "dotnet", new Dictionary<string, string?>(),
        Now, TelemetryResourceStatus.Active);

    private static OpenTelemetryIngestionContext TrustedContext() =>
        OpenTelemetryIngestionContext.Authenticated("deployment-credential:orders");

    private static ProviderRepositoryReference Repository() =>
        new(Ids.ProviderId, "987", "acme", "orders");

    private static ProviderOperation ProviderOperation(
        ProviderOperationKind kind,
        string idempotencyKey,
        object payload,
        Guid incidentId,
        Guid? attemptId = null) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Ids.WorkspaceId, ApplicationId = Ids.ApplicationId,
        ProviderConnectionId = Ids.ProviderId, IncidentId = incidentId, AttemptId = attemptId,
        Kind = kind, IdempotencyKey = idempotencyKey, PayloadJson = JsonSerializer.Serialize(payload),
        PayloadHash = Hex('7'), Status = ProviderOperationStatus.Leased, CreatedAt = Now, UpdatedAt = Now
    };

    private static RepairResultEnvelope RepairResult(Guid attemptId)
    {
        const string diff = "diff --git a/src/A.cs b/src/A.cs\n--- a/src/A.cs\n+++ b/src/A.cs\n@@ -1 +1 @@\n-old\n+new\n";
        return new(
            HealingContractVersions.AgentProtocol, attemptId, "run-1", 1, BaseRevision, BaseRevision,
            "reproduced", 1m, "A missing guard caused the failure.", diff, Sha('2'),
            [new("src/A.cs", "modified", "low")],
            new(true, true, "reproduced", "Failure reproduced.", ["dotnet test"]),
            new(true, "Regression added.", ["A.Tests.cs"], true, true),
            [new("test", "dotnet test", "passed", "Tests passed.", TimeSpan.FromSeconds(1))],
            ["low-risk"], "Revert commit.", new(10, 5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1),
            new(Now.AddSeconds(-2), Now), Now);
    }

    private static HealingOptions EnabledOptions() => new()
    {
        DiscoveryEnabled = true,
        RepairDispatchEnabled = true,
        VerificationEnabled = true,
        LeaseDuration = TimeSpan.FromMinutes(5),
        RetryDelay = TimeSpan.Zero,
        Budgets = new HealingBudgetOptions
        {
            MaxRepairAttempts = 2, MaxConcurrentOperations = 2, MaxInferenceUnits = 1_000,
            MaxRepositoryRuns = 2, TimeBudget = TimeSpan.FromMinutes(10)
        }
    };

    private static string Hex(char value) => new(value, 64);
    private static string Sha(char value) => $"sha256:{Hex(value)}";

    private sealed class StaticScopeResolver(HealingTelemetryScope scope) : IHealingTelemetryScopeResolver
    {
        public ValueTask<HealingTelemetryScope?> ResolveAsync(
            OpenTelemetryIngestionContext ingestionContext,
            TelemetryResource resource,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<HealingTelemetryScope?>(scope);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }

    private sealed class FakeRepairProvider : IRepairWorkProvider
    {
        public int WorkItemCalls { get; private set; }
        public ValueTask<ProviderWorkItemReference> UpsertWorkItemAsync(RepairWorkItemUpsertRequest request, CancellationToken cancellationToken = default)
        {
            WorkItemCalls++;
            return ValueTask.FromResult(new ProviderWorkItemReference(
                "issue-1", 1, new Uri("https://github.com/acme/orders/issues/1"), "open", "fake-request-1"));
        }
        public ValueTask<ProviderOperationReceipt> DispatchWorkflowAsync(RepairWorkflowDispatchRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderOperationReceipt(request.IdempotencyKey, "run-1", false, Now));
    }

    private sealed class FakePatchPublisher : ITrustedPatchPublisher
    {
        public ValueTask<ProviderPullRequestReference> PublishAsync(RepairPublicationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderPullRequestReference(
                "pr-12", 12, new Uri("https://github.com/acme/orders/pull/12"),
                HeadRevision, BaseRevision, false, "fake-request-2"));
    }

    private sealed record LifecycleIds(
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid RevisionId,
        Guid ConfigurationId,
        Guid ProviderId,
        Guid BindingId,
        Guid PathPolicyId,
        Guid EvidencePolicyId,
        Guid MergePolicyId,
        Guid ManifestId,
        Guid ComponentId)
    {
        public static LifecycleIds Create() => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }
}
