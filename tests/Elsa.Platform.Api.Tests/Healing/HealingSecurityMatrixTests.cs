using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class HealingSecurityMatrixTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T18:00:00Z");

    [Fact]
    public async Task Workspace_and_actor_permissions_fail_closed_without_cross_tenant_disclosure()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        await app.SeedHealingAsync();
        var owner = app.CreateTrustedWorkspaceClient("security-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Secured", null));
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        var environment = await environmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
        var configurationUri = $"/api/workspaces/{workspaceId:D}/healing/applications/{application.Id:D}/configuration";

        var readerId = await app.AddWorkspaceMemberAsync(workspaceId, "security-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("security-reader");
        (await reader.GetAsync(configurationUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerId, HealingPermissions.Read);
        (await reader.GetAsync(configurationUri)).StatusCode.Should().Be(HttpStatusCode.OK);
        var deployment = await reader.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/healing/applications/{application.Id:D}/environments/{environment!.Id:D}/deployment-observations",
            new HealingDeploymentObservationApiRequest(new string('a', 40), Now, "delivery-1", Sha('a')));
        deployment.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var outsider = app.CreateTrustedWorkspaceClient("security-outsider");
        (await outsider.GetAsync(configurationUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var outsiderWorkspaceId = await outsider.GetDefaultWorkspaceIdAsync();
        (await owner.GetAsync($"/api/workspaces/{outsiderWorkspaceId:D}/healing/applications/{application.Id:D}/configuration"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Confirmation_is_account_bound_expiring_and_single_use_in_the_persisted_store()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("confirmation-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        var accountId = Guid.NewGuid();
        const string target = "healing:stop:target";
        var issuing = new ConfirmationService(store, new FixedTimeProvider(Now));
        var expiring = await issuing.CreateConfirmationAsync(workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.HealingEmergencyStop, target, accountId, TimeSpan.FromMinutes(1)));

        var expired = await new ConfirmationService(store, new FixedTimeProvider(Now.AddMinutes(2)))
            .ConsumeConfirmationAsync(workspaceId, expiring.Id, accountId, ConfirmationActionType.HealingEmergencyStop, target);
        expired.Validation.Id.Should().Be("deployment.confirmation.expired");

        var oneUse = await issuing.CreateConfirmationAsync(workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.HealingEmergencyStop, target, accountId, TimeSpan.FromMinutes(5)));
        var first = await issuing.ConsumeConfirmationAsync(
            workspaceId, oneUse.Id, accountId, ConfirmationActionType.HealingEmergencyStop, target);
        var replay = await issuing.ConsumeConfirmationAsync(
            workspaceId, oneUse.Id, accountId, ConfirmationActionType.HealingEmergencyStop, target);
        first.Succeeded.Should().BeTrue();
        replay.Validation.Id.Should().Be("deployment.confirmation.used");
    }

    [Fact]
    public async Task Webhook_boundary_rejects_unverified_input_and_returns_recorded_replay_without_reprocessing()
    {
        var handler = new BoundaryWebhookHandler();
        await using var app = new PlatformApiTestApplication(configureServices: services =>
            services.AddSingleton<IHealingVerifiedWebhookHandler>(handler));
        var client = app.CreateClient();

        (await client.PostAsync("/api/integrations/github/webhooks",
            new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.RejectSignature = true;
        (await client.SendAsync(Webhook("delivery-invalid"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        handler.RejectSignature = false;
        var accepted = await client.SendAsync(Webhook("delivery-1"));
        var replay = await client.SendAsync(Webhook("delivery-1"));
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.ProcessedDeliveries.Should().ContainSingle().Which.Should().Be("delivery-1");
    }

    [Fact]
    public async Task Oidc_repository_substitution_fails_before_nonce_or_replay_state_is_consumed()
    {
        using var rsa = RSA.Create(2048);
        var replay = new WorkloadReplayStore();
        var validator = new GitHubWorkloadIdentityValidator(
            "elsa-platform-healing", new SigningKeyProvider(new RsaSecurityKey(rsa.ExportParameters(false))), replay,
            new FixedTimeProvider(Now));
        const string nonce = "nonce-with-at-least-thirty-two-characters";
        var expectation = WorkloadExpectation(nonce);

        var substituted = await validator.ValidateAsync(WorkloadToken(rsa, repositoryId: "attacker-repository"), nonce, expectation);
        var valid = await validator.ValidateAsync(WorkloadToken(rsa, repositoryId: "987"), nonce, expectation);

        substituted.ReasonCode.Should().Be(GitHubSecurityReasonCodes.IdentityInvalid);
        valid.Succeeded.Should().BeTrue("a rejected substitution must not consume the legitimate one-use exchange");
    }

    [Fact]
    public async Task Queued_provider_mutation_revalidates_current_binding_authority()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var provider = new RecordingRepairProvider();
        var handler = new GitHubUpsertWorkItemOperationHandler(
            provider, db, new FixedTimeProvider(Now),
            new HealingRepairAuthorityService(db, Options.Create(EnabledOptions())));
        var ids = AuthorityIds.Instance;
        var request = new RepairWorkItemUpsertRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(ids.ProviderId, "987", "acme", "app"),
            ids.IncidentId, ids.EpisodeId, "Safe title", "{}", Sha('1'), "work-item:security");
        var operation = Operation(ids, request);
        var substitutedRequest = request with
        {
            Repository = request.Repository with { RepositoryProviderId = "attacker-repository" },
            IdempotencyKey = "work-item:substituted"
        };
        var substituted = await handler.ExecuteAsync(Operation(ids, substitutedRequest));
        await db.SourceOwnershipBindings.ExecuteUpdateAsync(setters =>
            setters.SetProperty(x => x.Status, SourceOwnershipBindingStatus.Suspended));

        var result = await handler.ExecuteAsync(operation);

        substituted.Disposition.Should().Be(HealingOperationDisposition.DeadLettered);
        substituted.OutcomeCode.Should().Be("repository-authority-mismatch");
        result.Disposition.Should().Be(HealingOperationDisposition.DeadLettered);
        result.OutcomeCode.Should().Be("healing-authority-revoked");
        provider.UpsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task Any_affected_environment_kill_switch_blocks_episode_mutation()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var configurationId = await db.HealingConfigurations.Select(x => x.Id).SingleAsync();
        var stoppedEnvironmentId = Guid.NewGuid();
        db.HealingEnvironmentConfigurations.Add(new HealingEnvironmentConfiguration
        {
            Id = Guid.NewGuid(), HealingConfigurationId = configurationId,
            WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            EnvironmentId = stoppedEnvironmentId, RepairEnabled = true, EnvironmentKillSwitch = true,
            CreatedAt = Now, UpdatedAt = Now
        });
        db.EnvironmentImpacts.Add(new EnvironmentImpact
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            EpisodeId = ids.EpisodeId, EnvironmentId = stoppedEnvironmentId,
            FirstSeenAt = Now, LastSeenAt = Now, OccurrenceCount = 1, ProducingRevisionsJson = "[]",
            VerificationStatus = VerificationOutcome.PendingDeployment, OccurrenceThreshold = 1,
            ClassificationPolicyVersion = "1", ClassificationPolicyHash = Sha('c')
        });
        await db.SaveChangesAsync();
        var authority = new HealingRepairAuthorityService(db, Options.Create(EnabledOptions()));

        var allowed = await authority.CanMutateAsync(
            ids.WorkspaceId, ids.ApplicationId, ids.EpisodeId, ids.ProviderId, ids.IncidentId);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Attempt_authority_requires_the_active_episode_and_exact_mutation_stage()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var authority = new HealingRepairAuthorityService(db, Options.Create(EnabledOptions()));

        var compatible = await authority.CanMutateAttemptAsync(
            ids.WorkspaceId, ids.ApplicationId, ids.EpisodeId, ids.ProviderId,
            ids.AttemptId, RepairAttemptStatus.PullRequestOpen);
        var wrongStage = await authority.CanMutateAttemptAsync(
            ids.WorkspaceId, ids.ApplicationId, ids.EpisodeId, ids.ProviderId,
            ids.AttemptId, RepairAttemptStatus.Publishing);

        var nextEpisodeId = Guid.NewGuid();
        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = nextEpisodeId,
            WorkspaceId = ids.WorkspaceId,
            ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId,
            PreviousEpisodeId = ids.EpisodeId,
            OpenedAt = Now.AddMinutes(1),
            ProducingRevisionsJson = "[]",
            Outcome = IncidentEpisodeOutcome.Active
        });
        await db.SaveChangesAsync();
        await db.IncidentEpisodes.Where(x => x.Id == ids.EpisodeId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Outcome, IncidentEpisodeOutcome.Superseded)
            .SetProperty(x => x.ClosedAt, Now.AddMinutes(1)));
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ActiveEpisodeId, nextEpisodeId));

        var staleEpisode = await authority.CanMutateAttemptAsync(
            ids.WorkspaceId, ids.ApplicationId, ids.EpisodeId, ids.ProviderId,
            ids.AttemptId, RepairAttemptStatus.PullRequestOpen);

        compatible.Should().BeTrue();
        wrongStage.Should().BeFalse("a queued mutation is only valid for its exact lifecycle stage");
        staleEpisode.Should().BeFalse("an old attempt loses mutation authority when the incident advances");
    }

    [Fact]
    public async Task Publication_completion_does_not_regress_an_incident_that_advanced_during_provider_latency()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        await db.RepairPullRequests.ExecuteDeleteAsync();
        await db.RepairAttempts.Where(x => x.Id == ids.AttemptId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, RepairAttemptStatus.Publishing));
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, HealingIncidentStatus.Repairing));
        var nextEpisodeId = Guid.NewGuid();
        var publisher = new AdvancingPatchPublisher(async cancellationToken =>
        {
            db.IncidentEpisodes.Add(new IncidentEpisode
            {
                Id = nextEpisodeId,
                WorkspaceId = ids.WorkspaceId,
                ApplicationId = ids.ApplicationId,
                IncidentId = ids.IncidentId,
                PreviousEpisodeId = ids.EpisodeId,
                OpenedAt = Now.AddMinutes(1),
                ProducingRevisionsJson = "[]",
                Outcome = IncidentEpisodeOutcome.Active
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.IncidentEpisodes.Where(x => x.Id == ids.EpisodeId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Outcome, IncidentEpisodeOutcome.Superseded)
                .SetProperty(x => x.ClosedAt, Now.AddMinutes(1)), cancellationToken);
            await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ActiveEpisodeId, nextEpisodeId)
                .SetProperty(x => x.Status, HealingIncidentStatus.ReadyForRepair), cancellationToken);
            await db.RepairAttempts.Where(x => x.Id == ids.AttemptId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RepairAttemptStatus.Stopped), cancellationToken);
        });
        var request = PublicationRequest(ids);
        var handler = new GitHubPublishPullRequestOperationHandler(
            publisher,
            db,
            new HealingRepairAuthorityService(db, Options.Create(EnabledOptions())));

        var outcome = await handler.ExecuteAsync(
            Operation(ids, request, ProviderOperationKind.PublishPullRequest, ids.AttemptId));

        outcome.Disposition.Should().Be(HealingOperationDisposition.Completed);
        outcome.OutcomeCode.Should().Be("repair-pull-request-published-stale");
        var incident = await db.HealingIncidents.AsNoTracking().SingleAsync();
        incident.ActiveEpisodeId.Should().Be(nextEpisodeId);
        incident.Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
        (await db.RepairAttempts.AsNoTracking().SingleAsync()).Status.Should().Be(RepairAttemptStatus.Stopped);
        (await db.RepairPullRequests.AsNoTracking().SingleAsync()).AttemptId.Should().Be(ids.AttemptId,
            "the historical PR must remain correlated for late signed webhook observations");
    }

    [Fact]
    public async Task Queued_merge_revalidates_the_current_policy_hash_before_provider_access()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var provider = new RecordingMergeProvider();
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;
        var handler = new GitHubRequestMergeOperationHandler(
            provider, db, new HealingRepairAuthorityService(db, Options.Create(options)),
            new DeploymentSafetySource(), Options.Create(options));
        var snapshot = new PolicyEvaluationSnapshot(
            HealingContractVersions.PolicyProtocol, "1", Sha('c'), Sha('s'), PolicyDecisions.AllowAutomaticMerge,
            [new("all", PolicyGateState.Pass, "allowed")], Now);
        var request = new ProviderMergeRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(ids.ProviderId, "987", "acme", "app"),
            "12", new string('d', 40), snapshot, "merge:security");
        await db.MergePolicies.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PolicyHash, Sha('9')));

        var result = await handler.ExecuteAsync(Operation(ids, request, ProviderOperationKind.RequestMerge, ids.AttemptId));

        result.OutcomeCode.Should().Be("merge-policy-changed");
        provider.Calls.Should().Be(0);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Open);
        pullRequest.MergePolicyEvaluationId.Should().BeNull();
    }

    [Fact]
    public async Task Queued_merge_revalidates_trusted_deployment_safety_before_provider_access()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var provider = new RecordingMergeProvider();
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;
        var handler = new GitHubRequestMergeOperationHandler(
            provider,
            db,
            new HealingRepairAuthorityService(db, Options.Create(options)),
            new DeploymentSafetySource(RepairPolicyObservationState.Failed),
            Options.Create(options));

        var result = await handler.ExecuteAsync(
            Operation(ids, MergeRequest(ids), ProviderOperationKind.RequestMerge, ids.AttemptId));

        result.OutcomeCode.Should().Be("deployment-safety-changed");
        provider.Calls.Should().Be(0);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Open);
        pullRequest.MergePolicyEvaluationId.Should().BeNull();
    }

    [Fact]
    public async Task Queued_merge_superseded_by_check_webhook_completes_without_mutating_the_current_pull_request()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var provider = new RecordingMergeProvider();
        var delivery = Delivery(ids, "check-delivery", "check_run", "completed");
        db.ProviderWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var connection = await db.ProviderConnections.SingleAsync();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            action = "completed",
            repository = new { id = 987 },
            check_run = new
            {
                name = "build", head_sha = new string('d', 40), status = "completed",
                conclusion = "success", completed_at = Now
            }
        }));

        var webhookOutcome = await new PlatformHealingGitHubWebhookProcessor(
                db, new GitHubWebhookProcessor(), new FixedTimeProvider(Now))
            .ProcessAsync(connection, delivery.ProviderDeliveryId, delivery.Event, body);
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;
        var outcome = await new GitHubRequestMergeOperationHandler(
                provider, db, new HealingRepairAuthorityService(db, Options.Create(options)),
                new DeploymentSafetySource(), Options.Create(options))
            .ExecuteAsync(Operation(ids, MergeRequest(ids), ProviderOperationKind.RequestMerge, ids.AttemptId));

        webhookOutcome.Should().Be("check-observed");
        outcome.Disposition.Should().Be(HealingOperationDisposition.Completed);
        outcome.OutcomeCode.Should().Be("merge-operation-superseded");
        provider.Calls.Should().Be(0);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Open);
        pullRequest.MergePolicyEvaluationId.Should().BeNull();
        (await db.RepairAttempts.SingleAsync()).Status.Should().Be(RepairAttemptStatus.PullRequestOpen);
    }

    [Fact]
    public async Task Blocked_evaluation_can_be_re_evaluated_after_check_webhook_with_distinct_audit_identity()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var policy = await db.MergePolicies.SingleAsync();
        policy.PolicyHash = new string('c', 64);
        policy.AutomaticMergeEnabled = true;
        policy.RequiredChecksJson = "[\"build\"]";
        policy.IndependentVerifier = "verify";
        policy.ForbiddenChangeCategoriesJson = JsonSerializer.Serialize(
            AutoMergeEligibilityPolicy.RequiredForbiddenChangeCategories);
        policy.RequireRollbackOrStopCapability = true;
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState = PullRequestMergeState.Open;
        pullRequest.MergePolicyEvaluationId = null;
        await db.SaveChangesAsync();
        var store = new HealingStore(db);
        var mergeService = new HealingMergeService(
            new HealingMergeEvaluationStore(db),
            new HealingAuditService(store, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));
        var request = new HealingMergeEvaluationRequest(
            ids.WorkspaceId,
            ids.ApplicationId,
            ids.AttemptId,
            ids.PullRequestId,
            policy,
            MergeInput(checksPassed: false, '1'),
            ids.IncidentId,
            ids.EpisodeId);

        var blocked = await mergeService.EvaluateAsync(request);
        var delivery = Delivery(ids, "re-evaluation-check", "check_run", "completed");
        db.ProviderWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var connection = await db.ProviderConnections.SingleAsync();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            action = "completed",
            repository = new { id = 987 },
            check_run = new
            {
                name = "build", head_sha = new string('d', 40), status = "completed",
                conclusion = "success", completed_at = Now
            }
        }));
        var webhookOutcome = await new PlatformHealingGitHubWebhookProcessor(
                db, new GitHubWebhookProcessor(), new FixedTimeProvider(Now))
            .ProcessAsync(connection, delivery.ProviderDeliveryId, delivery.Event, body);
        var allowed = await mergeService.EvaluateAsync(request with { Input = MergeInput(checksPassed: true, '2') });

        blocked.AutomaticMergeAllowed.Should().BeFalse();
        webhookOutcome.Should().Be("check-observed");
        allowed.AutomaticMergeAllowed.Should().BeTrue();
        var evaluations = await db.PolicyEvaluations
            .Where(x => x.Id == blocked.Evaluation.Id || x.Id == allowed.Evaluation.Id)
            .ToArrayAsync();
        evaluations.Should().HaveCount(2);
        var audits = await db.Set<HealingAuditEvent>().AsNoTracking()
            .Where(x => x.EventType == "merge-eligibility-evaluated")
            .ToArrayAsync();
        audits.Should().HaveCount(2);
        audits.Select(x => x.CorrelationId).Should().BeEquivalentTo(
            [blocked.Evaluation.Id, allowed.Evaluation.Id]);
        audits.Should().OnlyContain(x => x.CausationId == ids.IncidentId);
    }

    [Fact]
    public async Task Queued_merge_superseded_by_merged_webhook_preserves_successful_repair_and_incident_state()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var provider = new RecordingMergeProvider();
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, HealingIncidentStatus.PullRequestOpen));
        var delivery = Delivery(ids, "merged-delivery", "pull_request", "closed");
        db.ProviderWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var connection = await db.ProviderConnections.SingleAsync();
        var mergedRevision = new string('e', 40);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            action = "closed",
            repository = new { id = 987 },
            pull_request = new
            {
                number = 12, draft = false, merged = true, merged_at = Now,
                merge_commit_sha = mergedRevision,
                head = new { @ref = $"elsa-healing/{ids.AttemptId:N}", sha = new string('d', 40) },
                @base = new { sha = new string('c', 40) }
            }
        }));

        var webhookOutcome = await new PlatformHealingGitHubWebhookProcessor(
                db, new GitHubWebhookProcessor(), new FixedTimeProvider(Now))
            .ProcessAsync(connection, delivery.ProviderDeliveryId, delivery.Event, body);
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;
        var outcome = await new GitHubRequestMergeOperationHandler(
                provider, db, new HealingRepairAuthorityService(db, Options.Create(options)),
                new DeploymentSafetySource(), Options.Create(options))
            .ExecuteAsync(Operation(ids, MergeRequest(ids), ProviderOperationKind.RequestMerge, ids.AttemptId));

        webhookOutcome.Should().Be("pull-request-merged");
        outcome.Disposition.Should().Be(HealingOperationDisposition.Completed);
        outcome.OutcomeCode.Should().Be("merge-operation-superseded");
        provider.Calls.Should().Be(0);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Merged);
        pullRequest.MergedRevision.Should().Be(mergedRevision);
        (await db.RepairAttempts.SingleAsync()).Status.Should().Be(RepairAttemptStatus.Succeeded);
        (await db.HealingIncidents.SingleAsync()).Status.Should().Be(HealingIncidentStatus.Merged);
    }

    [Fact]
    public async Task Historical_attempt_pull_request_webhook_does_not_regress_the_active_incident_episode()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var nextEpisodeId = Guid.NewGuid();
        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = nextEpisodeId,
            WorkspaceId = ids.WorkspaceId,
            ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId,
            PreviousEpisodeId = ids.EpisodeId,
            OpenedAt = Now.AddMinutes(1),
            ProducingRevisionsJson = "[]",
            Outcome = IncidentEpisodeOutcome.Active
        });
        db.ProviderWebhookDeliveries.Add(Delivery(ids, "historical-merged", "pull_request", "closed"));
        await db.SaveChangesAsync();
        await db.IncidentEpisodes.Where(x => x.Id == ids.EpisodeId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Outcome, IncidentEpisodeOutcome.Superseded)
            .SetProperty(x => x.ClosedAt, Now.AddMinutes(1)));
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ActiveEpisodeId, nextEpisodeId)
            .SetProperty(x => x.Status, HealingIncidentStatus.ReadyForRepair));
        var connection = await db.ProviderConnections.SingleAsync();

        var outcome = await new PlatformHealingGitHubWebhookProcessor(
                db, new GitHubWebhookProcessor(), new FixedTimeProvider(Now))
            .ProcessAsync(
                connection,
                "historical-merged",
                "pull_request",
                PullRequestBody(ids, isMerged: true));

        outcome.Should().Be("pull-request-merged");
        var incident = await db.HealingIncidents.AsNoTracking().SingleAsync();
        incident.ActiveEpisodeId.Should().Be(nextEpisodeId);
        incident.Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
        (await db.RepairPullRequests.AsNoTracking().SingleAsync()).MergeState.Should().Be(PullRequestMergeState.Merged);
    }

    [Fact]
    public async Task Pull_request_terminal_webhook_state_is_monotonic_and_idempotent()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, HealingIncidentStatus.PullRequestOpen));
        db.ProviderWebhookDeliveries.AddRange(
            Delivery(ids, "terminal-merged", "pull_request", "closed"),
            Delivery(ids, "duplicate-merged", "pull_request", "closed"),
            Delivery(ids, "late-reopened", "pull_request", "reopened"));
        await db.SaveChangesAsync();
        var connection = await db.ProviderConnections.SingleAsync();
        var processor = new PlatformHealingGitHubWebhookProcessor(
            db, new GitHubWebhookProcessor(), new FixedTimeProvider(Now));

        var merged = await processor.ProcessAsync(
            connection, "terminal-merged", "pull_request", PullRequestBody(ids, isMerged: true));
        var mergedVersion = (await db.RepairPullRequests.AsNoTracking().SingleAsync()).Version;
        var duplicate = await processor.ProcessAsync(
            connection, "duplicate-merged", "pull_request", PullRequestBody(ids, isMerged: true));
        var lateOpen = await processor.ProcessAsync(
            connection, "late-reopened", "pull_request", PullRequestBody(ids, isMerged: false, action: "reopened"));

        merged.Should().Be("pull-request-merged");
        duplicate.Should().Be("pull-request-merged");
        lateOpen.Should().Be("pull-request-observed");
        var pullRequest = await db.RepairPullRequests.AsNoTracking().SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Merged);
        pullRequest.MergedRevision.Should().Be(new string('e', 40));
        pullRequest.Version.Should().Equal(mergedVersion, "duplicate and older observations must be state-idempotent");
        (await db.RepairAttempts.AsNoTracking().SingleAsync()).Status.Should().Be(RepairAttemptStatus.Succeeded);
        (await db.HealingIncidents.AsNoTracking().SingleAsync()).Status.Should().Be(HealingIncidentStatus.Merged);
    }

    [Fact]
    public async Task Provider_terminal_state_after_merge_success_crash_does_not_roll_back_the_pending_webhook_projection()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var ids = AuthorityIds.Instance;
        var provider = new RecordingMergeProvider(new ProviderMergeSnapshot(
            "12", false, false, new string('d', 40), new string('c', 40), [], [], false, Now));
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;

        var outcome = await new GitHubRequestMergeOperationHandler(
                provider, db, new HealingRepairAuthorityService(db, Options.Create(options)),
                new DeploymentSafetySource(), Options.Create(options))
            .ExecuteAsync(Operation(ids, MergeRequest(ids), ProviderOperationKind.RequestMerge, ids.AttemptId));

        outcome.Disposition.Should().Be(HealingOperationDisposition.Completed);
        outcome.OutcomeCode.Should().Be("merge-provider-terminal-observed");
        provider.Calls.Should().Be(1);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.MergeRequested);
        pullRequest.MergePolicyEvaluationId.Should().Be(ids.EvaluationId);
        (await db.RepairAttempts.SingleAsync()).Status.Should().Be(RepairAttemptStatus.PullRequestOpen);
    }

    [Fact]
    public async Task Allowed_evaluation_left_on_open_pull_request_is_released_for_durable_merge_recovery()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedHealingAsync(SeedAuthorityAsync);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        await db.RepairPullRequests.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.MergeState, PullRequestMergeState.Open));
        var provider = new RecordingMergeProvider();
        var options = EnabledOptions();
        options.AutomaticMergeEnabled = true;
        var store = new HealingStore(db);
        var time = new FixedTimeProvider(Now.Add(options.LeaseDuration).AddSeconds(1));
        var audit = new HealingAuditService(store, time);
        var coordinator = new HealingAutoMergeCoordinator(
            db,
            new HealingMergeService(new HealingMergeEvaluationStore(db), audit, time),
            provider,
            scope.ServiceProvider.GetRequiredService<ITrustedDeploymentSafetyCapabilitySource>(),
            new ProviderOperationService(store, [], options, "merge-recovery-test", time),
            time,
            Options.Create(options));

        var recovered = await coordinator.RunOnceAsync();

        recovered.Should().BeTrue();
        provider.Calls.Should().Be(0);
        var pullRequest = await db.RepairPullRequests.SingleAsync();
        pullRequest.MergeState.Should().Be(PullRequestMergeState.Open);
        pullRequest.MergePolicyEvaluationId.Should().BeNull();
        (await db.PolicyEvaluations.SingleAsync()).Decision.Should().Be(PolicyDecision.AllowAutomaticMerge);
    }

    [Fact]
    public async Task Malicious_patch_and_token_shaped_audit_detail_never_reach_publisher_credentials_or_audit_store()
    {
        using var rsa = RSA.Create(2048);
        var tokenHandler = new CountingHttpHandler();
        using var http = new HttpClient(tokenHandler) { BaseAddress = new Uri("https://api.github.com/") };
        var context = PublicationContext(rsa);
        var repository = new RecordingRepositoryPublisher();
        var publisher = new TrustedGitHubPatchPublisher(
            new GitHubAppTokenProvider(http), new PublicationContextResolver(context), repository);
        var malicious = "diff --git a/src/a.cs b/../secret.cs\n--- a/src/a.cs\n+++ b/../secret.cs\n@@ -1 +1 @@\n-old\n+new\n";

        var publish = () => publisher.PublishAsync(PublicationRequest(context, malicious)).AsTask();
        await publish.Should().ThrowAsync<GitHubSecurityException>();
        tokenHandler.Count.Should().Be(0);
        repository.PublishCalls.Should().Be(0);

        var auditStore = new RecordingAuditStore();
        var audit = new HealingAuditService(auditStore, new FixedTimeProvider(Now));
        var write = new HealingAuditWrite(
            Guid.NewGuid(), "incident", Guid.NewGuid(), "security", "blocked", "platform", "test",
            Guid.NewGuid(), null, null, null, null,
            new Dictionary<string, string?> { ["outcomeCode"] = "github_pat_secret_material" });
        var append = () => audit.AppendAsync(write).AsTask();
        await append.Should().ThrowAsync<ArgumentException>();
        auditStore.AppendCalls.Should().Be(0);
    }

    private static async Task SeedAuthorityAsync(HealingDbContext db)
    {
        var ids = AuthorityIds.Instance;
        var configuration = new HealingConfiguration
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            DiscoveryEnabled = true, RepairEnabled = true, SignalProfileVersion = HealingContractVersions.SignalProfile,
            AutomaticMergeEnabled = true, DefaultAttemptLimit = 2, VerificationWindow = TimeSpan.FromMinutes(5), TimeBudget = TimeSpan.FromMinutes(10),
            ConcurrencyBudget = 2, InferenceBudget = 100, RepositoryRunBudget = 2, CreatedAt = Now, UpdatedAt = Now
        };
        configuration.Environments.Add(new HealingEnvironmentConfiguration
        {
            Id = Guid.NewGuid(), HealingConfigurationId = configuration.Id, WorkspaceId = ids.WorkspaceId,
            ApplicationId = ids.ApplicationId, EnvironmentId = ids.EnvironmentId, RepairEnabled = true,
            ClassificationPolicyJson = "{}", CreatedAt = Now, UpdatedAt = Now
        });
        db.HealingWorkspaceConfigurations.Add(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, CreatedAt = Now, UpdatedAt = Now
        });
        db.HealingConfigurations.Add(configuration);
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = ids.ProviderId, WorkspaceId = ids.WorkspaceId, Provider = "GitHub", InstallationId = "42",
            RepositoryProviderId = "987", RepositoryOwner = "acme", RepositoryName = "app",
            CredentialReference = "secret://github", Status = ProviderConnectionStatus.Active, CreatedAt = Now, UpdatedAt = Now
        });
        db.PathPolicies.Add(new PathPolicy
        {
            Id = ids.PathPolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, Name = "path",
            PolicyVersion = "1", PolicyHash = Sha('a'), AllowedRootsJson = "[\"src\"]", ForbiddenRootsJson = "[]",
            MaxFiles = 5, MaxChangedLines = 100, MaxPatchBytes = 10_000, CreatedAt = Now
        });
        db.EvidencePolicies.Add(new EvidencePolicy
        {
            Id = ids.EvidencePolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, Name = "evidence",
            PolicyVersion = "1", PolicyHash = Sha('b'), MaximumTier = EvidenceTier.DefaultRedacted,
            PermittedFieldsJson = "[]", CreatedAt = Now
        });
        db.MergePolicies.Add(new MergePolicy
        {
            Id = ids.MergePolicyId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, Name = "merge",
            PolicyVersion = "1", PolicyHash = Sha('c'), RequiredChecksJson = "[]",
            ForbiddenChangeCategoriesJson = "[]", CreatedAt = Now
        });
        db.SourceOwnershipBindings.Add(new SourceOwnershipBinding
        {
            Id = ids.BindingId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, Name = "app",
            SelectorKind = SourceSelectorKind.Application, SelectorPattern = "Acme.App", ProviderConnectionId = ids.ProviderId,
            RepositoryProviderId = "987", RepositoryOwner = "acme", RepositoryName = "app", TargetBranch = "main",
            WorkflowIdentity = ".github/workflows/heal.yml", WorkflowReference = "refs/heads/main", WorkflowRevision = new string('b', 40),
            PathPolicyId = ids.PathPolicyId, EvidencePolicyId = ids.EvidencePolicyId, MergePolicyId = ids.MergePolicyId,
            Status = SourceOwnershipBindingStatus.Active, CreatedAt = Now, UpdatedAt = Now
        });
        db.HealingIncidents.Add(new HealingIncident
        {
            Id = ids.IncidentId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            FingerprintVersion = "1", Fingerprint = Sha('f'), RepairRepositoryKey = "github:987",
            Status = HealingIncidentStatus.ReadyForRepair, Severity = IncidentSeverity.Error,
            Classification = IncidentClassification.UnhandledRequest, SelectedBindingId = ids.BindingId,
            FirstSeenAt = Now, LastSeenAt = Now, OccurrenceCount = 1
        });
        await db.SaveChangesAsync();
        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = ids.EpisodeId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, OpenedAt = Now, ProducingRevisionsJson = "[]", Outcome = IncidentEpisodeOutcome.Active
        });
        await db.SaveChangesAsync();
        db.EnvironmentImpacts.Add(new EnvironmentImpact
        {
            Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            EpisodeId = ids.EpisodeId, EnvironmentId = ids.EnvironmentId, FirstSeenAt = Now, LastSeenAt = Now,
            OccurrenceCount = 1, ProducingRevisionsJson = "[]", VerificationStatus = VerificationOutcome.PendingDeployment,
            OccurrenceThreshold = 1, ClassificationPolicyVersion = "1", ClassificationPolicyHash = Sha('c')
        });
        db.RepairWorkItemProjections.Add(new RepairWorkItemProjection
        {
            Id = ids.ProjectionId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, EpisodeId = ids.EpisodeId, ProviderConnectionId = ids.ProviderId,
            MachineSummaryHash = Sha('m'), ProjectionStatus = WorkItemProjectionStatus.Pending
        });
        db.EvidenceBundles.Add(new EvidenceBundle
        {
            Id = ids.EvidenceId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, Tier = EvidenceTier.DefaultRedacted, CanonicalJson = "{}", Digest = new string('8', 64),
            ProvenanceJson = "{}", OmissionsJson = "[]", SizeBytes = 2, CreatedAt = Now, ExpiresAt = Now.AddHours(1)
        });
        db.RepairAttempts.Add(new RepairAttempt
        {
            Id = ids.AttemptId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, EpisodeId = ids.EpisodeId, BindingId = ids.BindingId,
            AttemptNumber = 1, TargetRevision = new string('c', 40), Status = RepairAttemptStatus.PullRequestOpen,
            EvidenceBundleId = ids.EvidenceId, RepairClassification = RepairClassification.Reproduced,
            NonceHash = new string('7', 64), BudgetJson = "{}", UsageJson = "{}"
        });
        db.PolicyEvaluations.Add(new PolicyEvaluation
        {
            Id = ids.EvaluationId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            AttemptId = ids.AttemptId, PolicyId = ids.MergePolicyId, PolicyKind = PolicyKind.Merge,
            PolicyVersion = "1", PolicyHash = Sha('c'), InputSnapshotHash = Sha('s'), GateResultsJson = "[]",
            Decision = PolicyDecision.AllowAutomaticMerge, ReasonCodesJson = "[]", EvaluatedAt = Now
        });
        db.RepairPullRequests.Add(new RepairPullRequest
        {
            Id = ids.PullRequestId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            AttemptId = ids.AttemptId, ProviderConnectionId = ids.ProviderId, ProviderPullRequestId = "pr-12",
            Number = 12, Url = "https://github.com/acme/app/pull/12", Branch = $"elsa-healing/{ids.AttemptId:N}",
            BaseRevision = new string('c', 40), HeadRevision = new string('d', 40), PatchDigest = Sha('p'),
            IsDraft = false, Classification = RepairClassification.Reproduced, CheckSnapshotJson = "{}",
            BranchProtectionSnapshotJson = "{}", MergePolicyEvaluationId = ids.EvaluationId,
            MergeState = PullRequestMergeState.MergeRequested
        });
        await db.SaveChangesAsync();
        await db.HealingIncidents.Where(x => x.Id == ids.IncidentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ActiveEpisodeId, ids.EpisodeId)
            .SetProperty(x => x.WorkItemProjectionId, ids.ProjectionId));
    }

    private static ProviderOperation Operation(AuthorityIds ids, RepairWorkItemUpsertRequest request) =>
        Operation(ids, request, ProviderOperationKind.UpsertWorkItem);

    private static ProviderOperation Operation(
        AuthorityIds ids,
        object request,
        ProviderOperationKind kind,
        Guid? attemptId = null) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
        ProviderConnectionId = ids.ProviderId, IncidentId = ids.IncidentId, AttemptId = attemptId, Kind = kind,
        IdempotencyKey = kind switch
        {
            ProviderOperationKind.RequestMerge => "merge:security",
            ProviderOperationKind.PublishPullRequest => ((RepairPublicationRequest)request).IdempotencyKey,
            _ => ((RepairWorkItemUpsertRequest)request).IdempotencyKey
        },
        PayloadJson = JsonSerializer.Serialize(request), PayloadHash = Sha('p'),
        Status = ProviderOperationStatus.Leased, CreatedAt = Now, UpdatedAt = Now
    };

    private static HealingOptions EnabledOptions() => new()
    {
        RepairDispatchEnabled = true,
        Budgets = new HealingBudgetOptions { MaxRepairAttempts = 2, MaxConcurrentOperations = 2, MaxInferenceUnits = 100, MaxRepositoryRuns = 2, TimeBudget = TimeSpan.FromMinutes(10) }
    };

    private static AutoMergeEligibilityInput MergeInput(bool checksPassed, char digest) => new(
        new string(digest, 64),
        AutoMergeEligibilityPolicy.RequiredGates.Select(gate =>
            gate == AutoMergePolicyGates.RequiredChecks && !checksPassed
                ? new RepairPolicyObservation(gate, RepairPolicyObservationState.Failed, "required-checks-failed")
                : RepairPolicyObservation.Satisfied(gate, $"{gate}-satisfied")).ToArray());

    private static ProviderMergeRequest MergeRequest(AuthorityIds ids) => new(
        HealingContractVersions.ProviderProtocol,
        new ProviderRepositoryReference(ids.ProviderId, "987", "acme", "app"),
        "12",
        new string('d', 40),
        new PolicyEvaluationSnapshot(
            HealingContractVersions.PolicyProtocol, "1", Sha('c'), Sha('s'),
            PolicyDecisions.AllowAutomaticMerge, [new("all", PolicyGateState.Pass, "allowed")], Now),
        "merge:security");

    private static ProviderWebhookDelivery Delivery(
        AuthorityIds ids,
        string deliveryId,
        string eventName,
        string action) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = ids.WorkspaceId, ProviderDeliveryId = deliveryId,
        InstallationId = "42", RepositoryProviderId = "987", Event = eventName, Action = action,
        BodyDigest = Sha('7'), ReceivedAt = Now, Status = ProviderWebhookDeliveryStatus.Pending
    };

    private static byte[] PullRequestBody(
        AuthorityIds ids,
        bool isMerged,
        string action = "closed") =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            action,
            repository = new { id = 987 },
            pull_request = new
            {
                number = 12,
                draft = false,
                merged = isMerged,
                merged_at = isMerged ? Now : (DateTimeOffset?)null,
                merge_commit_sha = isMerged ? new string('e', 40) : null,
                head = new { @ref = $"elsa-healing/{ids.AttemptId:N}", sha = new string('d', 40) },
                @base = new { sha = new string('c', 40) }
            }
        }));

    private static HttpRequestMessage Webhook(string deliveryId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/github/webhooks")
        {
            Content = new StringContent("{\"action\":\"opened\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=test");
        request.Headers.Add("X-GitHub-Delivery", deliveryId);
        request.Headers.Add("X-GitHub-Event", "pull_request");
        return request;
    }

    private static GitHubWorkloadIdentityExpectation WorkloadExpectation(string nonce) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Hash(nonce), "repo:acme/app:ref:refs/heads/main",
        "987", "acme", "app", "acme/app/.github/workflows/heal.yml@refs/heads/main", new string('b', 40),
        "refs/heads/main", new string('c', 40));

    private static string WorkloadToken(RSA rsa, string repositoryId)
    {
        var claims = new Dictionary<string, string>
        {
            ["sub"] = "repo:acme/app:ref:refs/heads/main", ["repository_id"] = repositoryId,
            ["repository"] = "acme/app", ["workflow_ref"] = "acme/app/.github/workflows/heal.yml@refs/heads/main",
            ["workflow_sha"] = new string('b', 40), ["ref"] = "refs/heads/main", ["sha"] = new string('c', 40),
            ["run_id"] = "123", ["run_attempt"] = "1", ["actor_id"] = "99", ["jti"] = "jti-valid"
        }.Select(x => new Claim(x.Key, x.Value)).Append(new Claim("iat", Now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            GitHubWorkloadIdentityValidator.GitHubIssuer, "elsa-platform-healing", claims,
            Now.AddMinutes(-1).UtcDateTime, Now.AddMinutes(5).UtcDateTime,
            new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)));
    }

    private static TrustedGitHubPublicationContext PublicationContext(RSA rsa) => new(
        new GitHubRepositoryAuthorization(Guid.NewGuid(), "987", "acme", "app", "42",
            new GitHubAppCredential("123", rsa.ExportRSAPrivateKeyPem()), new Dictionary<string, GitHubApprovedWorkflow>()),
        new TrustedGitHubPublicationPolicy("1", Sha('a'), ["src"], [], 5, 100, 100_000));

    private static RepairPublicationRequest PublicationRequest(TrustedGitHubPublicationContext context, string diff)
    {
        var attemptId = Guid.NewGuid();
        var result = new RepairResultEnvelope(
            HealingContractVersions.AgentProtocol, attemptId, "run", 1, new string('c', 40), new string('c', 40),
            "reproduced", 1m, "safe", diff, Sha('d'), [], new(true, true, "reproduced", "safe", []),
            new(true, "safe", []), [], [], "revert", new(1, 1, TimeSpan.Zero, TimeSpan.Zero),
            new(Now, Now), Now);
        return new RepairPublicationRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(context.Authorization.ProviderConnectionId, "987", "acme", "app"),
            Guid.NewGuid(), Guid.NewGuid(), attemptId, "main", new string('c', 40), result,
            new(HealingContractVersions.PolicyProtocol, "1", Sha('a'), Sha('i'), PolicyDecisions.AllowPublication,
                [new("path", PolicyGateState.Pass, "allowed")], Now), "publish:security");
    }

    private static RepairPublicationRequest PublicationRequest(AuthorityIds ids)
    {
        var result = new RepairResultEnvelope(
            HealingContractVersions.AgentProtocol,
            ids.AttemptId,
            "run",
            1,
            new string('c', 40),
            new string('c', 40),
            "reproduced",
            1m,
            "safe",
            "diff --git a/src/a.cs b/src/a.cs\n",
            Sha('d'),
            [],
            new(true, true, "reproduced", "safe", []),
            new(true, "safe", []),
            [],
            [],
            "revert",
            new(1, 1, TimeSpan.Zero, TimeSpan.Zero),
            new(Now, Now),
            Now);
        return new RepairPublicationRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(ids.ProviderId, "987", "acme", "app"),
            ids.IncidentId,
            ids.EpisodeId,
            ids.AttemptId,
            "main",
            new string('c', 40),
            result,
            new(HealingContractVersions.PolicyProtocol, "1", Sha('a'), Sha('i'), PolicyDecisions.AllowPublication,
                [new("path", PolicyGateState.Pass, "allowed")], Now),
            "publish:security");
    }

    private static string Sha(char value) => $"sha256:{new string(value, 64)}";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BoundaryWebhookHandler : IHealingVerifiedWebhookHandler
    {
        private readonly HashSet<string> _deliveries = new(StringComparer.Ordinal);
        public bool RejectSignature { get; set; }
        public IReadOnlyCollection<string> ProcessedDeliveries => _deliveries;

        public ValueTask<HealingVerifiedWebhookReceipt> ProcessAsync(HealingVerifiedWebhookRequest request, CancellationToken cancellationToken = default)
        {
            if (RejectSignature)
                throw new HealingWorkflowRequestException(HttpStatusCode.Unauthorized, "healing.webhook.signature-invalid");
            var accepted = _deliveries.Add(request.DeliveryId);
            return ValueTask.FromResult(new HealingVerifiedWebhookReceipt(request.DeliveryId, !accepted, accepted ? "accepted" : "replay"));
        }
    }

    private sealed class SigningKeyProvider(SecurityKey key) : IGitHubOidcSigningKeyProvider
    {
        public ValueTask<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<SecurityKey>>([key]);
        public void RequestRefresh() { }
    }

    private sealed class WorkloadReplayStore : IGitHubWorkloadReplayStore
    {
        private readonly HashSet<string> _accepted = new(StringComparer.Ordinal);
        public ValueTask<bool> TryAcceptAsync(GitHubWorkloadReplayRecord exchange, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_accepted.Add(exchange.JwtId) && _accepted.Add(exchange.NonceHash));
    }

    private sealed class RecordingRepairProvider : IRepairWorkProvider
    {
        public int UpsertCalls { get; private set; }
        public ValueTask<ProviderWorkItemReference> UpsertWorkItemAsync(RepairWorkItemUpsertRequest request, CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return ValueTask.FromResult(new ProviderWorkItemReference("1", 1, new Uri("https://github.com/acme/app/issues/1"), "open", null));
        }
        public ValueTask<ProviderOperationReceipt> DispatchWorkflowAsync(RepairWorkflowDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMergeProvider(ProviderMergeSnapshot? snapshot = null) : IRepairMergeProvider
    {
        public int Calls { get; private set; }
        public ValueTask<ProviderMergeSnapshot> GetMergeSnapshotAsync(ProviderRepositoryReference repository, string pullRequestId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return snapshot is null
                ? throw new InvalidOperationException("Policy drift must be rejected before provider reads.")
                : ValueTask.FromResult(snapshot);
        }
        public ValueTask<ProviderOperationReceipt> RequestMergeAsync(ProviderMergeRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Policy drift must be rejected before provider mutation.");
        }
    }

    private sealed class DeploymentSafetySource(
        RepairPolicyObservationState state = RepairPolicyObservationState.Satisfied) :
        ITrustedDeploymentSafetyCapabilitySource
    {
        public ValueTask<TrustedDeploymentSafetyCapabilitySnapshot> GetAsync(
            Guid workspaceId,
            Guid applicationId,
            Guid episodeId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new TrustedDeploymentSafetyCapabilitySnapshot(
                new string('a', 64),
                state,
                state == RepairPolicyObservationState.Satisfied
                    ? "trusted-deployment-rollback-available"
                    : "trusted-deployment-rollback-unavailable"));
    }

    private sealed class CountingHttpHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class PublicationContextResolver(TrustedGitHubPublicationContext context) : ITrustedGitHubPublicationContextResolver
    {
        public ValueTask<TrustedGitHubPublicationContext?> ResolveAsync(RepairPublicationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TrustedGitHubPublicationContext?>(context);
    }

    private sealed class RecordingRepositoryPublisher : ITrustedGitHubRepositoryPublisher
    {
        public int PublishCalls { get; private set; }
        public ValueTask<string?> GetTargetRevisionAsync(GitHubRepositoryAuthorization authorization, string targetBranch, string installationToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(new string('c', 40));
        public ValueTask<bool> IsCommitReachableAsync(GitHubRepositoryAuthorization authorization, string ancestorRevision, string targetRevision, string installationToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
        public ValueTask<ProviderPullRequestReference> PublishAsync(GitHubRepositoryAuthorization authorization, string installationToken, TrustedGitHubPatchPlan plan, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed class AdvancingPatchPublisher(Func<CancellationToken, Task> advance) : ITrustedPatchPublisher
    {
        public async ValueTask<ProviderPullRequestReference> PublishAsync(
            RepairPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            await advance(cancellationToken);
            return new ProviderPullRequestReference(
                "pr-99",
                99,
                new Uri("https://github.com/acme/app/pull/99"),
                new string('d', 40),
                new string('c', 40),
                false,
                "request-99");
        }
    }

    private sealed class RecordingAuditStore : IHealingAuditStore
    {
        public int AppendCalls { get; private set; }
        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            return ValueTask.FromResult(auditEvent);
        }
        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(Elsa.Platform.Healing.Core.Security.HealingAuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>([]);
    }

    private sealed record AuthorityIds(
        Guid WorkspaceId, Guid ApplicationId, Guid EnvironmentId, Guid ProviderId, Guid BindingId,
        Guid IncidentId, Guid EpisodeId, Guid ProjectionId)
    {
        public Guid PathPolicyId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000009");
        public Guid EvidencePolicyId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000010");
        public Guid MergePolicyId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000011");
        public Guid EvidenceId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000012");
        public Guid AttemptId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000013");
        public Guid EvaluationId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000014");
        public Guid PullRequestId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000015");
        public static AuthorityIds Instance { get; } = new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000003"), Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Guid.Parse("10000000-0000-0000-0000-000000000005"), Guid.Parse("10000000-0000-0000-0000-000000000006"),
            Guid.Parse("10000000-0000-0000-0000-000000000007"), Guid.Parse("10000000-0000-0000-0000-000000000008"));
    }
}
