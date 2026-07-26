using System.Net;
using System.Text.Json;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Workflows.RuntimeApplier;
using FluentAssertions;

namespace ValenceControl.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowRuntimeCommandHttpClientTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CommandId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = WorkspaceId,
        EngineId = EngineId,
        WorkerId = "worker-a",
        ClaimLeaseDuration = TimeSpan.FromSeconds(120)
    };

    [Fact]
    public async Task Polls_runtime_command_endpoint_and_maps_commands()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $$"""
            {
              "commands": [
                {{CommandJson("Pending")}}
              ]
            }
            """);
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var commands = await client.PollAsync(limit: 5);

        handler.RequestUri.Should().Be(new Uri($"https://control.example.test/api/workspaces/{WorkspaceId:D}/deployments/runtime/engines/{EngineId:D}/commands?limit=5"));
        commands.Should().ContainSingle();
        var command = commands.Single();
        command.Id.Should().Be(CommandId);
        command.Status.Should().Be(WorkflowRuntimeCommandStatus.Pending);
        command.Artifact!.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        command.Artifact.ContentDigest.Should().Be(new ArtifactDigest("sha256", new string('a', 64)));
    }

    [Fact]
    public async Task Rejects_malformed_poll_success_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "not-json", "text/plain");
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var act = () => client.PollAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Control runtime command poll response could not be read.");
    }

    [Fact]
    public async Task Claims_command_with_worker_identity_and_lease()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $$"""
            {
              "leaseToken": "lease-1",
              "command": {{CommandJson("Claimed", workerId: "worker-a")}}
            }
            """);
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var result = await client.ClaimAsync(CommandId);

        result.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        result.Succeeded.Should().BeTrue();
        result.Claim!.LeaseToken.Should().Be("lease-1");
        result.Claim.Command.Status.Should().Be(WorkflowRuntimeCommandStatus.Claimed);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        body.RootElement.GetProperty("engineId").GetGuid().Should().Be(EngineId);
        body.RootElement.GetProperty("workerId").GetString().Should().Be("worker-a");
        body.RootElement.GetProperty("leaseSeconds").GetInt32().Should().Be(120);
    }

    [Fact]
    public async Task Maps_claim_conflict_to_safe_result()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, """{"title":"Bearer token rejected by lease owner"}""");
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var result = await client.ClaimAsync(CommandId);

        result.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Conflict);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().NotContain("Bearer");
        result.Message.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task Maps_malformed_success_response_to_retryable_result()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "not-json", "text/plain");
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var result = await client.ClaimAsync(CommandId);

        result.Status.Should().Be(WorkflowRuntimeCommandClientStatus.RetryableError);
        result.Message.Should().Be("Control claim response could not be read.");
    }

    [Fact]
    public async Task Maps_unknown_command_values_to_unknown_states()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $$"""
            {
              "commands": [
                {{CommandJson("NewFutureStatus", action: "NewFutureAction")}}
              ]
            }
            """);
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var command = (await client.PollAsync()).Single();

        command.Action.Should().Be(WorkflowRuntimeCommandAction.Unknown);
        command.Status.Should().Be(WorkflowRuntimeCommandStatus.Unknown);
    }

    [Fact]
    public async Task Reports_progress_and_completion_with_safe_payloads()
    {
        var handler = new QueueHandler(
            new QueuedResponse(HttpStatusCode.OK, CommandJson("Running", percentComplete: 50, progressMessage: "Applying")),
            new QueuedResponse(HttpStatusCode.OK, CommandJson("Completed", runtimeReference: "elsa://workflows/payment-retry")));
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var progress = await client.ReportProgressAsync(CommandId, "lease-1", "applying", 50, "Applying");
        var complete = await client.CompleteAsync(
            CommandId,
            "lease-1",
            new ArtifactDigest("sha256", new string('b', 64)),
            "elsa://workflows/payment-retry",
            [new WorkflowArtifactDiagnostic("apply", WorkflowArtifactDiagnosticSeverity.Error, "password leaked")]);

        progress.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        progress.Command!.PercentComplete.Should().Be(50);
        complete.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        complete.Command!.RuntimeReference.Should().Be("elsa://workflows/payment-retry");
        handler.Requests.Should().HaveCount(2);
        using var completeBody = JsonDocument.Parse(handler.Requests[1].Body!);
        completeBody.RootElement.GetProperty("observedArtifactDigest").GetProperty("value").GetString().Should().Be(new string('b', 64));
        completeBody.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString().Should().Be("[redacted] leaked");
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("reject")]
    public async Task Reports_terminal_failure_states_with_safe_diagnostics(string action)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CommandJson(action == "fail" ? "Failed" : "Rejected"));
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);
        var diagnostics = new[]
        {
            new WorkflowArtifactDiagnostic("invalid", WorkflowArtifactDiagnosticSeverity.Warning, "private key unavailable")
        };

        var result = action == "fail"
            ? await client.FailAsync(CommandId, "lease-1", diagnostics)
            : await client.RejectAsync(CommandId, "lease-1", diagnostics);

        result.Status.Should().Be(WorkflowRuntimeCommandClientStatus.Succeeded);
        handler.RequestUri!.AbsolutePath.Should().EndWith($"/{action}");
        using var body = JsonDocument.Parse(handler.RequestBody!);
        body.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString().Should().Be("[redacted] unavailable");
    }

    [Fact]
    public async Task Sanitizes_diagnostics_returned_by_control()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, CommandJson("Failed", diagnosticMessage: "bearer token returned"));
        var client = new WorkflowRuntimeCommandHttpClient(new HttpClient(handler), _options);

        var result = await client.FailAsync(CommandId, "lease-1", []);

        result.Command!.Diagnostics.Single().Message.Should().Be("[redacted] [redacted] returned");
    }

    private static string CommandJson(
        string status,
        string action = "Deploy",
        string? workerId = null,
        int? percentComplete = null,
        string? progressMessage = null,
        string? runtimeReference = null,
        string? diagnosticMessage = null) =>
        $$"""
        {
          "id": "{{CommandId:D}}",
          "workspaceId": "{{WorkspaceId:D}}",
          "runId": "40000000-0000-0000-0000-000000000001",
          "environmentId": "50000000-0000-0000-0000-000000000001",
          "engineId": "{{EngineId:D}}",
          "action": "{{action}}",
          "status": "{{status}}",
          "artifact": {
            "artifactRecordId": "60000000-0000-0000-0000-000000000001",
            "artifactId": "elsa.workflow-definition:payment-retry:{{new string('a', 64)}}",
            "artifactTypeId": "elsa.workflow-definition",
            "contentDigest": { "algorithm": "sha256", "value": "{{new string('a', 64)}}" }
          },
          "revision": { "revisionId": "70000000-0000-0000-0000-000000000001" },
          "idempotencyKey": "deploy-payment-retry",
          "workerId": {{JsonValue(workerId)}},
          "claimedAt": null,
          "leaseExpiresAt": null,
          "heartbeatAt": null,
          "attemptNumber": 1,
          "percentComplete": {{percentComplete?.ToString() ?? "null"}},
          "progressMessage": {{JsonValue(progressMessage)}},
          "observedArtifactDigest": null,
          "runtimeReference": {{JsonValue(runtimeReference)}},
          "diagnostics": {{DiagnosticsJson(diagnosticMessage)}},
          "createdAt": "2026-05-29T08:00:00Z",
          "updatedAt": "2026-05-29T08:00:00Z",
          "availableAt": null,
          "expiresAt": null,
          "completedAt": null
        }
        """;

    private static string JsonValue(string? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value);

    private static string DiagnosticsJson(string? message) =>
        message is null
            ? "[]"
            : $$"""[{ "code": "remote", "severity": "Error", "message": {{JsonSerializer.Serialize(message)}} }]""";

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content, string contentType = "application/json") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, contentType)
            };
        }
    }

    private sealed class QueueHandler(params QueuedResponse[] responses) : HttpMessageHandler
    {
        private readonly Queue<QueuedResponse> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record QueuedResponse(HttpStatusCode StatusCode, string Content);

    private sealed record RecordedRequest(Uri Uri, string? Body);
}
