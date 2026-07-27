using System.Net;
using ValenceControl.Api.Healing;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ValenceControl.Api.Tests.Healing;

public sealed class ControlHealingWorkloadDurabilityTests
{
    private static readonly string Revision = new('a', 40);

    [Theory]
    [InlineData("control")]
    [InlineData("workspace")]
    [InlineData("application")]
    [InlineData("environment")]
    [InlineData("provider")]
    [InlineData("binding")]
    [InlineData("episode")]
    public async Task Revoked_live_authority_invalidates_and_durably_revokes_an_outstanding_capability(
        string revokedAuthority)
    {
        var provider = new RecordingProposalProvider();
        await using var app = Application(provider, revokedAuthority == "control");
        var ids = WorkloadIds.Create();
        await app.SeedHealingAsync(db => SeedAsync(db, ids));
        const string capability = "capability-with-enough-entropy-for-this-test";
        await using (var seedScope = app.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<HealingDbContext>();
            seedDb.WorkloadIdentityExchanges.Add(new WorkloadIdentityExchange
            {
                Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
                AttemptId = ids.AttemptId, Phase = "initial", ScopesJson = "[\"evidence.read\"]",
                Issuer = "issuer", Audience = "audience", Subject = "subject", RepositoryProviderId = "987",
                RepositoryOwner = "acme", RepositoryName = "app", WorkflowReference = "workflow",
                WorkflowRevision = Revision, SourceReference = "refs/heads/main", SourceRevision = Revision,
                WorkflowRunId = "1", ActorId = "2", JwtId = "jti-authority", NonceHash = Hash("nonce-authority"),
                IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ExchangedAt = DateTimeOffset.UtcNow,
                CapabilityTokenHash = ControlHealingWorkloadRequestAuthorizer.Hash(capability),
                Status = WorkloadIdentityExchangeStatus.Exchanged
            });
            await seedDb.SaveChangesAsync();
            await RevokeAuthorityAsync(seedDb, revokedAuthority);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IHealingWorkloadRequestAuthorizer>();
        var result = await authorizer.AuthorizeAsync(new(
            ids.WorkspaceId, ids.AttemptId, capability, WorkloadCapabilityScopes.ReadEvidence));

        Assert.False(result.Authorized);
        var dbContext = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var exchange = await dbContext.WorkloadIdentityExchanges.SingleAsync();
        Assert.Equal(WorkloadIdentityExchangeStatus.Revoked, exchange.Status);
        Assert.Null(exchange.CapabilityTokenHash);
    }

    [Fact]
    public async Task Concurrent_proposal_requests_share_one_durable_reservation_and_invoke_inference_once()
    {
        var provider = new RecordingProposalProvider(block: true);
        await using var app = Application(provider);
        var ids = WorkloadIds.Create();
        await app.SeedHealingAsync(db => SeedAsync(db, ids));
        var request = ProposalRequest(ids.AttemptId);

        await using var firstScope = app.Services.CreateAsyncScope();
        var firstApi = firstScope.ServiceProvider.GetRequiredService<IHealingWorkloadApi>();
        var first = firstApi.CreateProposalAsync(request).AsTask();
        await provider.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var secondScope = app.Services.CreateAsyncScope();
        var secondApi = secondScope.ServiceProvider.GetRequiredService<IHealingWorkloadApi>();
        var second = () => secondApi.CreateProposalAsync(request).AsTask();
        var conflict = await Assert.ThrowsAsync<HealingWorkflowRequestException>(second);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("healing.workload.inference-reservation-active", conflict.ReasonCode);

        provider.Release.TrySetResult();
        var response = await first;
        Assert.False(response.IsReplay);
        Assert.Equal(1, provider.CallCount);
        await using var assertScope = app.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<HealingDbContext>();
        Assert.Equal(
            ManagedRepairInferenceReservationStatus.Completed,
            (await db.ManagedRepairInferenceReservations.SingleAsync()).Status);
        Assert.Equal(1, (await db.ManagedRepairProposals.CountAsync()));
        Assert.Equal(1, (await db.HealingAuditEventsForTest().CountAsync(x => x.EventType == "repair-proposal-created")));
    }

    [Fact]
    public async Task Expired_crash_window_fails_closed_without_reinvoking_and_moves_work_to_audited_needs_human()
    {
        var provider = new RecordingProposalProvider();
        await using var app = Application(provider);
        var ids = WorkloadIds.Create();
        await app.SeedHealingAsync(async db =>
        {
            await SeedAsync(db, ids);
            db.ManagedRepairInferenceReservations.Add(new ManagedRepairInferenceReservation
            {
                Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
                AttemptId = ids.AttemptId, IdempotencyKey = "proposal-1",
                SourceContextDigest = ProposalRequest(ids.AttemptId).SourceContext.Digest,
                ReservedInferenceUnits = 100,
                LeaseTokenHash = ControlHealingWorkloadRequestAuthorizer.Hash("expired-inference-lease"),
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = ManagedRepairInferenceReservationStatus.Leased,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
        });

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var recovered = await ControlManagedInferenceRecovery.RecoverExpiredAsync(db, TimeProvider.System);
        var replayedRecovery = await ControlManagedInferenceRecovery.RecoverExpiredAsync(db, TimeProvider.System);

        Assert.InRange(recovered, 0, 1);
        Assert.Equal(0, replayedRecovery);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(
            ManagedRepairInferenceReservationStatus.Abandoned,
            (await db.ManagedRepairInferenceReservations.SingleAsync()).Status);
        var attempt = await db.RepairAttempts.SingleAsync();
        Assert.Equal(RepairAttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.LeaseToken);
        Assert.Equal("managed-inference-outcome-indeterminate", attempt.OutcomeCode);
        var incident = await db.HealingIncidents.SingleAsync();
        Assert.Equal(HealingIncidentStatus.NeedsHuman, incident.Status);
        Assert.Equal(NeedsHumanReason.PolicyBlocked, incident.NeedsHumanReason);
        Assert.Equal("repair-inference-abandoned", (await db.HealingAuditEventsForTest().SingleAsync()).EventType);
    }

    [Fact]
    public async Task Proposal_replay_repairs_a_missing_audit_without_reinvoking_inference()
    {
        var provider = new RecordingProposalProvider();
        await using var app = Application(provider);
        var ids = WorkloadIds.Create();
        var request = ProposalRequest(ids.AttemptId);
        await app.SeedHealingAsync(db => SeedAsync(db, ids, RepairAttemptStatus.ProposalReady));
        await using (var seedScope = app.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<HealingDbContext>();
            var protector = seedScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("ValenceControl.Healing.ProposalFinalizationNonce.v1");
            seedDb.ManagedRepairProposals.Add(StoredProposal(ids, request, protector.Protect("stored-finalization-nonce")));
            await seedDb.SaveChangesAsync();
        }

        await using var scope = app.Services.CreateAsyncScope();
        var response = await scope.ServiceProvider.GetRequiredService<IHealingWorkloadApi>()
            .CreateProposalAsync(request);

        Assert.True(response.IsReplay);
        Assert.Equal(0, provider.CallCount);
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        Assert.Equal("repair-proposal-created", (await db.HealingAuditEventsForTest().SingleAsync()).EventType);
        var second = await scope.ServiceProvider.GetRequiredService<IHealingWorkloadApi>().CreateProposalAsync(request);
        Assert.True(second.IsReplay);
        Assert.Equal(1, (await db.HealingAuditEventsForTest().CountAsync()));
    }

    [Fact]
    public async Task Result_replay_repairs_a_missing_audit_without_changing_terminal_state()
    {
        var provider = new RecordingProposalProvider();
        await using var app = Application(provider);
        var ids = WorkloadIds.Create();
        var proposalId = Guid.NewGuid();
        var proposalDigest = Hash("proposal");
        var envelope = ResultEnvelope(ids.AttemptId, proposalId, proposalDigest);
        var envelopeDigest = RepairAgentGateway.ComputeSha256Digest(
            System.Text.Json.JsonSerializer.Serialize(envelope));
        await app.SeedHealingAsync(async db =>
        {
            await SeedAsync(db, ids, RepairAttemptStatus.ResultReceived);
            db.RepairResults.Add(new RepairResult
            {
                Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
                AttemptId = ids.AttemptId, ProposalId = proposalId, ProposalDigest = proposalDigest,
                IdempotencyKey = "result-1", WorkflowRunId = envelope.WorkflowRunId,
                WorkflowRunAttempt = envelope.WorkflowRunAttempt, BaseRevision = Revision, TargetRevision = Revision,
                Classification = RepairClassification.InferredHighConfidence, Confidence = envelope.Confidence,
                UnifiedDiff = envelope.UnifiedDiff, PatchDigest = envelope.PatchDigest,
                EnvelopeDigest = envelopeDigest, ChangedPathsJson = "[]", ReproductionJson = "{}",
                RegressionJson = "{}", ValidationJson = "[]", RiskJson = "{}", SubmittedAt = DateTimeOffset.UtcNow
            });
        });

        await using var scope = app.Services.CreateAsyncScope();
        var receipt = await scope.ServiceProvider.GetRequiredService<IHealingWorkloadApi>()
            .UploadResultAsync(new(
                HealingContractVersions.WorkloadProtocol,
                ids.AttemptId,
                "result-1",
                envelope));

        Assert.True(receipt.IsReplay);
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        Assert.Equal(RepairAttemptStatus.ResultReceived, (await db.RepairAttempts.SingleAsync()).Status);
        Assert.Equal(1, (await db.RepairResults.CountAsync()));
        Assert.Equal("repair-result-accepted", (await db.HealingAuditEventsForTest().SingleAsync()).EventType);
    }

    private static ControlApiTestApplication Application(
        IRepairProposalProvider provider,
        bool controlKillSwitch = false) => new(
        new Dictionary<string, string?>
        {
            ["Healing:RepairDispatchEnabled"] = "true",
            ["Healing:ControlKillSwitch"] = controlKillSwitch.ToString()
        },
        services =>
        {
            services.RemoveAll<IRepairProposalProvider>();
            services.AddSingleton(provider);
        });

    private static Task RevokeAuthorityAsync(HealingDbContext db, string revokedAuthority) =>
        revokedAuthority switch
        {
            "control" => Task.CompletedTask,
            "workspace" => db.HealingWorkspaceConfigurations.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.WorkspaceKillSwitch, true)),
            "application" => db.HealingConfigurations.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.ApplicationKillSwitch, true)),
            "environment" => db.HealingEnvironmentConfigurations.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.EnvironmentKillSwitch, true)),
            "provider" => db.ProviderConnections.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.Status, ProviderConnectionStatus.Suspended)),
            "binding" => db.SourceOwnershipBindings.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.Status, SourceOwnershipBindingStatus.Suspended)),
            "episode" => db.IncidentEpisodes.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.Outcome, IncidentEpisodeOutcome.Superseded)),
            _ => throw new ArgumentOutOfRangeException(nameof(revokedAuthority))
        };

    private static async Task SeedAsync(
        HealingDbContext db,
        WorkloadIds ids,
        RepairAttemptStatus attemptStatus = RepairAttemptStatus.Running)
    {
        var now = DateTimeOffset.UtcNow;
        var configuration = new HealingConfiguration
        {
            Id = ids.ConfigurationId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            DiscoveryEnabled = true, RepairEnabled = true, SignalProfileVersion = HealingContractVersions.SignalProfile,
            DefaultAttemptLimit = 2, VerificationWindow = TimeSpan.FromMinutes(5), TimeBudget = TimeSpan.FromMinutes(10),
            ConcurrencyBudget = 2, InferenceBudget = 100, RepositoryRunBudget = 2,
            CreatedAt = now, UpdatedAt = now
        };
        configuration.Environments.Add(new HealingEnvironmentConfiguration
        {
            Id = Guid.NewGuid(), HealingConfigurationId = configuration.Id, WorkspaceId = ids.WorkspaceId,
            ApplicationId = ids.ApplicationId, EnvironmentId = ids.EnvironmentId, RepairEnabled = true,
            CreatedAt = now, UpdatedAt = now
        });
        db.HealingWorkspaceConfigurations.Add(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, CreatedAt = now, UpdatedAt = now
        });
        db.HealingConfigurations.Add(configuration);
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = ids.ProviderId, WorkspaceId = ids.WorkspaceId, Provider = "GitHub", InstallationId = "42",
            RepositoryProviderId = "987", RepositoryOwner = "acme", RepositoryName = "app",
            CredentialReference = "secret://github", Status = ProviderConnectionStatus.Active,
            CreatedAt = now, UpdatedAt = now
        });
        db.PathPolicies.Add(new PathPolicy
        {
            Id = ids.PathPolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            Name = "path", PolicyVersion = "1", PolicyHash = Hash("path"), AllowedRootsJson = "[\"src\"]",
            ForbiddenRootsJson = "[]", MaxFiles = 5, MaxChangedLines = 100, MaxPatchBytes = 10_000, CreatedAt = now
        });
        db.EvidencePolicies.Add(new EvidencePolicy
        {
            Id = ids.EvidencePolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            Name = "evidence", PolicyVersion = "1", PolicyHash = Hash("evidence"),
            MaximumTier = EvidenceTier.DefaultRedacted, PermittedFieldsJson = "[]", CreatedAt = now
        });
        db.MergePolicies.Add(new MergePolicy
        {
            Id = ids.MergePolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            Name = "merge", PolicyVersion = "1", PolicyHash = Hash("merge"), RequiredChecksJson = "[]",
            ForbiddenChangeCategoriesJson = "[]", CreatedAt = now
        });
        db.SourceOwnershipBindings.Add(new SourceOwnershipBinding
        {
            Id = ids.BindingId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, Name = "app",
            SelectorKind = SourceSelectorKind.Application, SelectorPattern = "Acme.App", ProviderConnectionId = ids.ProviderId,
            RepositoryProviderId = "987", RepositoryOwner = "acme", RepositoryName = "app", TargetBranch = "main",
            WorkflowIdentity = ".github/workflows/heal.yml", WorkflowReference = "refs/heads/main",
            WorkflowRevision = Revision, PathPolicyId = ids.PathPolicyId, EvidencePolicyId = ids.EvidencePolicyId,
            MergePolicyId = ids.MergePolicyId, Status = SourceOwnershipBindingStatus.Active,
            CreatedAt = now, UpdatedAt = now
        });
        db.HealingIncidents.Add(new HealingIncident
        {
            Id = ids.IncidentId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            FingerprintVersion = "1", Fingerprint = Hash("incident"), Status = HealingIncidentStatus.Repairing,
            Severity = IncidentSeverity.Error, Classification = IncidentClassification.UnhandledRequest,
            SelectedBindingId = ids.BindingId, FirstSeenAt = now, LastSeenAt = now, OccurrenceCount = 1
        });
        await db.SaveChangesAsync();
        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = ids.EpisodeId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, OpenedAt = now, ProducingRevisionsJson = "[]",
            Outcome = IncidentEpisodeOutcome.Active
        });
        await db.SaveChangesAsync();
        db.EnvironmentImpacts.Add(new EnvironmentImpact
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            EpisodeId = ids.EpisodeId, EnvironmentId = ids.EnvironmentId, FirstSeenAt = now, LastSeenAt = now,
            OccurrenceCount = 1, ProducingRevisionsJson = "[]", VerificationStatus = VerificationOutcome.PendingDeployment,
            OccurrenceThreshold = 1, ClassificationPolicyVersion = "1", ClassificationPolicyHash = Hash("classification")
        });
        var evidenceJson = "{}";
        db.EvidenceBundles.Add(new EvidenceBundle
        {
            Id = ids.EvidenceId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, Tier = EvidenceTier.DefaultRedacted, CanonicalJson = evidenceJson,
            Digest = RepairAgentGateway.ComputeSha256Digest(evidenceJson), ProvenanceJson = "{}", OmissionsJson = "[]",
            SizeBytes = 2, CreatedAt = now, ExpiresAt = now.AddHours(1)
        });
        db.RepairAttempts.Add(new RepairAttempt
        {
            Id = ids.AttemptId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, EpisodeId = ids.EpisodeId, BindingId = ids.BindingId,
            AttemptNumber = 1, ProducingRevision = Revision, TargetRevision = Revision, Status = attemptStatus,
            EvidenceBundleId = ids.EvidenceId, RepairClassification = RepairClassification.Reproduced,
            NonceHash = Hash("nonce"), BudgetJson = "{\"maxDurationSeconds\":600,\"maxTokens\":100,\"maxSteps\":2}",
            UsageJson = "{}", LeaseOwner = attemptStatus == RepairAttemptStatus.Running ? "runner" : null,
            LeaseToken = attemptStatus == RepairAttemptStatus.Running ? "attempt-lease" : null,
            LeaseExpiresAt = attemptStatus == RepairAttemptStatus.Running ? now.AddMinutes(10) : null,
            StartedAt = now
        });
        await db.SaveChangesAsync();
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(x => x.ActiveEpisodeId, ids.EpisodeId));
    }

    private static WorkloadProposalCreateRequest ProposalRequest(Guid attemptId)
    {
        const string content = "public sealed class Orders { }";
        var file = new RepairSourceFile("src/Orders.cs", content, RepairAgentGateway.ComputeSha256Digest(content));
        var source = new RepairSourceContextBundle(Revision, string.Empty, [file], []);
        source = source with { Digest = RepairProposalProtocol.ComputeSourceContextDigest(source) };
        return new(
            HealingContractVersions.WorkloadProtocol,
            attemptId,
            "proposal-1",
            new(source.TargetRevision, source.Digest,
                source.Files.Select(x => new WorkloadRepairSourceFile(x.Path, x.Content, x.Digest, x.IsTruncated)).ToArray(),
                source.OmittedPaths));
    }

    private static ManagedRepairProposal StoredProposal(
        WorkloadIds ids,
        WorkloadProposalCreateRequest request,
        string protectedNonce)
    {
        var payload = new ControlHealingWorkloadApi.StoredManagedProposal(
            Revision, Revision, RepairAgentClassifications.InferredHighConfidence, .9m, "Likely cause.", string.Empty,
            RepairAgentGateway.ComputeSha256Digest(string.Empty), [], [], "Revert the change.",
            new(10, 5, TimeSpan.FromSeconds(1), TimeSpan.Zero, 0));
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        const string nonce = "stored-finalization-nonce";
        return new ManagedRepairProposal
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            AttemptId = ids.AttemptId, IdempotencyKey = request.IdempotencyKey,
            SourceContextDigest = request.SourceContext.Digest,
            ProposalDigest = RepairAgentGateway.ComputeSha256Digest(json), ProposalJson = json,
            FinalizationNonceHash = ControlHealingWorkloadRequestAuthorizer.Hash(nonce),
            ProtectedFinalizationNonce = protectedNonce,
            Status = ManagedRepairProposalStatus.Ready, CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private static RepairResultEnvelope ResultEnvelope(
        Guid attemptId,
        Guid proposalId,
        string proposalDigest)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            HealingContractVersions.AgentProtocol,
            attemptId,
            "workflow-run",
            1,
            Revision,
            Revision,
            RepairAgentClassifications.InferredHighConfidence,
            .9m,
            "A missing guard is the likely cause.",
            string.Empty,
            RepairAgentGateway.ComputeSha256Digest(string.Empty),
            [],
            new(false, false, "not-reproduced", "Not reproduced.", []),
            new(false, "No regression test added.", []),
            [],
            [],
            "Revert the change.",
            new(10, 5, TimeSpan.FromSeconds(1), TimeSpan.Zero),
            new(now.AddSeconds(-1), now),
            now,
            proposalId,
            proposalDigest);
    }

    private static string Hash(string value) => RepairAgentGateway.ComputeSha256Digest(value);

    private sealed class RecordingProposalProvider(bool block = false) : IRepairProposalProvider
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RepairProposal> ProposeAsync(
            RepairProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Invoked.TrySetResult();
            if (block)
                await Release.Task.WaitAsync(cancellationToken);
            return new(
                RepairAgentClassifications.InferredHighConfidence,
                .9m,
                "A missing guard is the likely cause.",
                string.Empty,
                [],
                [],
                "Revert the change.",
                new(10, 5, TimeSpan.FromSeconds(1)));
        }
    }

    private sealed record WorkloadIds(
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid ConfigurationId,
        Guid ProviderId,
        Guid BindingId,
        Guid PathPolicyId,
        Guid EvidencePolicyId,
        Guid MergePolicyId,
        Guid IncidentId,
        Guid EpisodeId,
        Guid EvidenceId,
        Guid AttemptId)
    {
        public static WorkloadIds Create() => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }
}

internal static class HealingDbContextWorkloadTestExtensions
{
    public static IQueryable<HealingAuditEvent> HealingAuditEventsForTest(this HealingDbContext dbContext) =>
        dbContext.Set<HealingAuditEvent>();
}
