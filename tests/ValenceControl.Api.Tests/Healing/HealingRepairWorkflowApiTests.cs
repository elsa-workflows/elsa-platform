using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ValenceControl.Api.Healing;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingRepairWorkflowApiTests
{
    [Fact]
    public async Task Oidc_exchange_returns_only_an_incident_scoped_capability()
    {
        var workload = new RecordingWorkloadApi();
        await using var app = CreateApplication(workload);
        var client = app.CreateClient();
        var attemptId = Guid.NewGuid();

        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{WorkspaceId:D}/healing/workload/exchange",
            new WorkloadIdentityExchangeRequest(
                HealingContractVersions.WorkloadProtocol,
                attemptId,
                "one-time-nonce",
                "signed-github-oidc-assertion"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var grant = await response.Content.ReadControlJsonAsync<HealingWorkloadCapabilityResponse>();
        grant!.AttemptId.Should().Be(attemptId);
        grant.AllowedScopes.Should().BeEquivalentTo(
            WorkloadCapabilityScopes.ReadEvidence,
            WorkloadCapabilityScopes.CreateProposal);
        grant.CapabilityToken.Should().Be("incident-capability");
        workload.ExchangeRequest.Should().NotBeNull();
    }

    [Fact]
    public async Task Evidence_heartbeat_and_result_require_the_bound_capability_scope()
    {
        var workload = new RecordingWorkloadApi();
        var authorizer = new RecordingCapabilityAuthorizer();
        await using var app = CreateApplication(workload, authorizer);
        var client = app.CreateClient();
        var attemptId = workload.AttemptId;
        var baseUri = $"/api/workspaces/{WorkspaceId:D}/healing/workload/attempts/{attemptId:D}";

        (await client.GetAsync($"{baseUri}/evidence")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        authorizer.DeniedScope = WorkloadCapabilityScopes.HeartbeatAttempt;
        using var evidence = Authorized(HttpMethod.Get, $"{baseUri}/evidence");
        (await client.SendAsync(evidence)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var heartbeat = Authorized(
            HttpMethod.Post,
            $"{baseUri}/heartbeat",
            new WorkloadHeartbeatRequest(HealingContractVersions.WorkloadProtocol, attemptId, "heartbeat-1", DateTimeOffset.UtcNow));
        (await client.SendAsync(heartbeat)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        authorizer.DeniedScope = null;
        using var acceptedHeartbeat = Authorized(
            HttpMethod.Post,
            $"{baseUri}/heartbeat",
            new WorkloadHeartbeatRequest(HealingContractVersions.WorkloadProtocol, attemptId, "heartbeat-2", DateTimeOffset.UtcNow));
        (await client.SendAsync(acceptedHeartbeat)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var result = Authorized(
            HttpMethod.Post,
            $"{baseUri}/result",
            new WorkloadResultUploadRequest(HealingContractVersions.WorkloadProtocol, attemptId, "result-1", Result(attemptId)));
        (await client.SendAsync(result)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        authorizer.Requests.Select(x => x.RequiredScope).Should().Contain(
            WorkloadCapabilityScopes.ReadEvidence,
            WorkloadCapabilityScopes.HeartbeatAttempt,
            WorkloadCapabilityScopes.UploadResult);
        workload.HeartbeatRequest!.AttemptId.Should().Be(attemptId);
        workload.ResultRequest!.Result.Reproduction.Classification.Should().Be("not-reproduced");
        workload.ResultRequest.Result.Confidence.Should().Be(0.93m);
    }

    [Fact]
    public async Task Proposal_creation_and_finalization_exchange_use_disjoint_capabilities()
    {
        var workload = new RecordingWorkloadApi();
        var authorizer = new RecordingCapabilityAuthorizer();
        await using var app = CreateApplication(workload, authorizer);
        var client = app.CreateClient();
        var attemptId = workload.AttemptId;
        var content = "namespace Acme; public sealed class Broken { }";
        var fileDigest = RepairAgentGateway.ComputeSha256Digest(content);
        var source = new RepairSourceContextBundle(
            "target-revision",
            string.Empty,
            [new RepairSourceFile("src/Broken.cs", content, fileDigest)],
            []);
        source = source with { Digest = RepairProposalProtocol.ComputeSourceContextDigest(source) };
        var request = new WorkloadProposalCreateRequest(
            HealingContractVersions.WorkloadProtocol,
            attemptId,
            "proposal-once",
            new(source.TargetRevision, source.Digest,
                source.Files.Select(x => new WorkloadRepairSourceFile(x.Path, x.Content, x.Digest, x.IsTruncated)).ToArray(),
                source.OmittedPaths));
        using var create = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/workspaces/{WorkspaceId:D}/healing/workload/attempts/{attemptId:D}/proposal")
        {
            Content = JsonContent.Create(request)
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "incident-capability");

        var createResponse = await client.SendAsync(create);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        workload.ProposalRequest.Should().BeEquivalentTo(request);
        authorizer.Requests.Should().ContainSingle(x => x.RequiredScope == WorkloadCapabilityScopes.CreateProposal);
        var proposal = await createResponse.Content.ReadControlJsonAsync<WorkloadProposalCreateResponse>();
        proposal!.Proposal.ProposalId.Should().Be(workload.ProposalId);

        var finalizeResponse = await client.PostControlJsonAsync(
            $"/api/workspaces/{WorkspaceId:D}/healing/workload/attempts/{attemptId:D}/proposals/{workload.ProposalId:D}/finalize-exchange",
            new WorkloadProposalFinalizationExchangeRequest(
                HealingContractVersions.WorkloadProtocol,
                attemptId,
                workload.ProposalId,
                "finalization-nonce",
                "second-oidc-assertion"));

        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var grant = await finalizeResponse.Content.ReadControlJsonAsync<HealingWorkloadCapabilityResponse>();
        grant!.AllowedScopes.Should().BeEquivalentTo(
            WorkloadCapabilityScopes.FinalizeProposal,
            WorkloadCapabilityScopes.UploadResult);
        workload.FinalizationRequest.Should().NotBeNull();
    }

    [Fact]
    public async Task Route_attempt_mismatch_is_rejected_before_any_workload_operation()
    {
        var workload = new RecordingWorkloadApi();
        await using var app = CreateApplication(workload);
        var client = app.CreateClient();
        var routeAttempt = Guid.NewGuid();
        using var request = Authorized(
            HttpMethod.Post,
            $"/api/workspaces/{WorkspaceId:D}/healing/workload/attempts/{routeAttempt:D}/heartbeat",
            new WorkloadHeartbeatRequest(HealingContractVersions.WorkloadProtocol, Guid.NewGuid(), "mismatch", DateTimeOffset.UtcNow));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        workload.HeartbeatRequest.Should().BeNull();
    }

    [Fact]
    public async Task Result_requires_consistent_explicit_reproduction_and_bounded_confidence()
    {
        var workload = new RecordingWorkloadApi();
        await using var app = CreateApplication(workload);
        var client = app.CreateClient();
        var attemptId = workload.AttemptId;
        var invalid = Result(attemptId) with
        {
            Confidence = 1.1m,
            Reproduction = new(true, true, "not-reproduced", "Contradictory reproduction evidence.", [])
        };
        using var request = Authorized(
            HttpMethod.Post,
            $"/api/workspaces/{WorkspaceId:D}/healing/workload/attempts/{attemptId:D}/result",
            new WorkloadResultUploadRequest(HealingContractVersions.WorkloadProtocol, attemptId, "invalid-result", invalid));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        workload.ResultRequest.Should().BeNull();
    }

    [Fact]
    public async Task Webhook_requires_verification_headers_and_passes_the_unmodified_body_to_the_handler()
    {
        var webhook = new RecordingVerifiedWebhookHandler();
        await using var app = CreateApplication(new RecordingWorkloadApi(), webhook: webhook);
        var client = app.CreateClient();
        const string body = "{\"action\":\"opened\",\"repository\":{\"id\":42}}";

        (await client.PostAsync("/api/integrations/github/webhooks", new StringContent(body, Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/github/webhooks")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=verified");
        request.Headers.Add("X-GitHub-Delivery", "delivery-42");
        request.Headers.Add("X-GitHub-Event", "pull_request");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Encoding.UTF8.GetString(webhook.Request!.RawBody).Should().Be(body);
        webhook.Request.DeliveryId.Should().Be("delivery-42");
        webhook.Request.Event.Should().Be("pull_request");
    }

    [Fact]
    public async Task Invalid_or_replayed_webhook_is_fail_closed_and_idempotent()
    {
        var webhook = new RecordingVerifiedWebhookHandler { RejectSignature = true };
        await using var app = CreateApplication(new RecordingWorkloadApi(), webhook: webhook);
        var client = app.CreateClient();

        (await client.SendAsync(WebhookRequest("invalid-delivery"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        webhook.RejectSignature = false;
        webhook.IsReplay = true;

        var replay = await client.SendAsync(WebhookRequest("known-delivery"));

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = await replay.Content.ReadControlJsonAsync<HealingVerifiedWebhookReceipt>();
        receipt!.IsReplay.Should().BeTrue();
    }

    private static ControlApiTestApplication CreateApplication(
        RecordingWorkloadApi workload,
        RecordingCapabilityAuthorizer? authorizer = null,
        RecordingVerifiedWebhookHandler? webhook = null) =>
        new(configureServices: services =>
        {
            services.AddSingleton<IHealingWorkloadApi>(workload);
            services.AddSingleton<IHealingWorkloadRequestAuthorizer>(authorizer ?? new RecordingCapabilityAuthorizer());
            services.AddSingleton<IHealingVerifiedWebhookHandler>(webhook ?? new RecordingVerifiedWebhookHandler());
        });

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "incident-capability");
        if (body is not null)
            request.Content = JsonContent.Create(body, options: ControlApiTestApplication.JsonOptions);
        return request;
    }

    private static HttpRequestMessage WebhookRequest(string deliveryId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/github/webhooks")
        {
            Content = new StringContent("{\"action\":\"opened\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=value");
        request.Headers.Add("X-GitHub-Delivery", deliveryId);
        request.Headers.Add("X-GitHub-Event", "pull_request");
        return request;
    }

    private static RepairResultEnvelope Result(Guid attemptId) => new(
        HealingContractVersions.AgentProtocol,
        attemptId,
        "run-42",
        1,
        "base",
        "target",
        "inferred-high-confidence",
        0.93m,
        "Safe causal summary.",
        "diff --git a/a b/a",
        "sha256:patch",
        [new("src/A.cs", "modified", "application-code")],
        new(true, false, "not-reproduced", "Reproduction did not succeed.", ["dotnet test"]),
        new(true, "Regression test added.", ["tests/A.cs"]),
        [new("test", "dotnet test", "passed", "Focused tests passed.", TimeSpan.FromSeconds(1))],
        ["low-risk"],
        "Revert the commit.",
        new(10, 5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        new(DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow),
        DateTimeOffset.UtcNow,
        Guid.Parse("00000000-0000-0000-0000-000000000202"),
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    private static readonly Guid WorkspaceId = Guid.Parse("00000000-0000-0000-0000-000000000101");

    private sealed class RecordingWorkloadApi : IHealingWorkloadApi
    {
        public Guid AttemptId { get; } = Guid.NewGuid();
        public WorkloadIdentityExchangeRequest? ExchangeRequest { get; private set; }
        public Guid ProposalId { get; } = Guid.NewGuid();
        public WorkloadProposalCreateRequest? ProposalRequest { get; private set; }
        public WorkloadProposalFinalizationExchangeRequest? FinalizationRequest { get; private set; }
        public WorkloadHeartbeatRequest? HeartbeatRequest { get; private set; }
        public WorkloadResultUploadRequest? ResultRequest { get; private set; }

        public ValueTask<WorkloadCapabilityGrant> ExchangeAsync(WorkloadIdentityExchangeRequest request, CancellationToken cancellationToken = default)
        {
            ExchangeRequest = request;
            return ValueTask.FromResult(new WorkloadCapabilityGrant(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                "incident-capability",
                new HashSet<string>(
                    [WorkloadCapabilityScopes.ReadEvidence, WorkloadCapabilityScopes.CreateProposal],
                    StringComparer.Ordinal),
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public ValueTask<WorkloadEvidenceResponse> GetEvidenceAsync(WorkloadEvidenceRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkloadEvidenceResponse(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                new(
                    HealingContractVersions.AgentProtocol,
                    request.AttemptId,
                    "default-redacted",
                    "{}",
                    RepairAgentGateway.ComputeSha256Digest("{}"),
                    ["exception.message"],
                    DateTimeOffset.UtcNow.AddMinutes(5)),
                new RepairAgentBudget(TimeSpan.FromMinutes(30), 100_000, 2)));

        public ValueTask<WorkloadProposalCreateResponse> CreateProposalAsync(
            WorkloadProposalCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            ProposalRequest = request;
            var now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new WorkloadProposalCreateResponse(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                new(
                    HealingContractVersions.WorkloadProtocol,
                    request.AttemptId,
                    ProposalId,
                    "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    request.SourceContext.Digest,
                    "base-revision",
                    request.SourceContext.TargetRevision,
                    "inferred-high-confidence",
                    0.9m,
                    "Cause",
                    "diff --git a/a b/a\n",
                    RepairAgentGateway.ComputeSha256Digest("diff --git a/a b/a\n"),
                    [new("src/Broken.cs", "modified", "low")],
                    ["low risk"],
                    "Revert",
                    new(10, 5, TimeSpan.FromSeconds(1), TimeSpan.Zero),
                    now,
                    now.AddMinutes(30)),
                "finalization-nonce",
                false));
        }

        public ValueTask<WorkloadCapabilityGrant> ExchangeFinalizationAsync(
            WorkloadProposalFinalizationExchangeRequest request,
            CancellationToken cancellationToken = default)
        {
            FinalizationRequest = request;
            return ValueTask.FromResult(new WorkloadCapabilityGrant(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                "final-capability",
                new HashSet<string>(
                    [WorkloadCapabilityScopes.FinalizeProposal, WorkloadCapabilityScopes.UploadResult],
                    StringComparer.Ordinal),
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public ValueTask<WorkloadHeartbeatReceipt> HeartbeatAsync(WorkloadHeartbeatRequest request, CancellationToken cancellationToken = default)
        {
            HeartbeatRequest = request;
            return ValueTask.FromResult(new WorkloadHeartbeatReceipt(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                DateTimeOffset.UtcNow.AddMinutes(2),
                false));
        }

        public ValueTask<WorkloadResultUploadReceipt> UploadResultAsync(WorkloadResultUploadRequest request, CancellationToken cancellationToken = default)
        {
            ResultRequest = request;
            return ValueTask.FromResult(new WorkloadResultUploadReceipt(
                HealingContractVersions.WorkloadProtocol,
                request.AttemptId,
                "sha256:result",
                false,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class RecordingCapabilityAuthorizer : IHealingWorkloadRequestAuthorizer
    {
        public string? DeniedScope { get; set; }
        public List<HealingWorkloadAuthorizationRequest> Requests { get; } = [];

        public ValueTask<HealingWorkloadAuthorizationResult> AuthorizeExchangeAsync(
            Guid workspaceId,
            Guid attemptId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                workspaceId == WorkspaceId
                    ? HealingWorkloadAuthorizationResult.Allow()
                    : HealingWorkloadAuthorizationResult.Deny("healing.workload.workspace.denied"));

        public ValueTask<HealingWorkloadAuthorizationResult> AuthorizeAsync(
            HealingWorkloadAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(
                request.CapabilityToken == "incident-capability" && request.RequiredScope != DeniedScope
                    ? HealingWorkloadAuthorizationResult.Allow()
                    : HealingWorkloadAuthorizationResult.Deny("healing.workload.capability.denied"));
        }
    }

    private sealed class RecordingVerifiedWebhookHandler : IHealingVerifiedWebhookHandler
    {
        public HealingVerifiedWebhookRequest? Request { get; private set; }
        public bool RejectSignature { get; set; }
        public bool IsReplay { get; set; }

        public ValueTask<HealingVerifiedWebhookReceipt> ProcessAsync(
            HealingVerifiedWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            if (RejectSignature)
                throw new HealingWorkflowRequestException(HttpStatusCode.Unauthorized, "healing.webhook.signature-invalid");
            return ValueTask.FromResult(new HealingVerifiedWebhookReceipt(request.DeliveryId, IsReplay, "accepted"));
        }
    }
}
