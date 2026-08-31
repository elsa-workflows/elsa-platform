using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ElsaControl.Deployment.Proof;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class ElsaHttpWorkflowProbeTests
{
    private static readonly DeploymentProofEnvironment Environment = new(
        "disposable-proof",
        "westeurope",
        "azure",
        ["runtime-login"]);

    [Fact]
    public async Task Runs_health_login_create_publish_verify_execute_and_poll_using_safe_evidence()
    {
        var handler = new RecordingHandler(
            _ =>
            [
                Json(HttpStatusCode.OK),
                Json(HttpStatusCode.OK),
                Json(HttpStatusCode.OK, "{\"isAuthenticated\":true,\"accessToken\":\"bearer-secret\"}"),
                Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\"}}"),
                Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}}"),
                Json(HttpStatusCode.OK, "{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}"),
                Json(HttpStatusCode.OK, "{}", new Dictionary<string, string> { ["x-elsa-workflow-instance-id"] = "proof-instance_01" }),
                Json(HttpStatusCode.OK, "{\"status\":\"Running\",\"incidentCount\":0,\"finishedAt\":null}"),
                Json(HttpStatusCode.OK, "{\"status\":\"Finished\",\"incidentCount\":0,\"finishedAt\":\"2026-08-31T12:00:00Z\"}")
            ]);
        using var client = new HttpClient(handler);
        var probe = new ElsaHttpWorkflowProbe(
            client,
            new ElsaHttpWorkflowProbeOptions(
                "proof-user",
                requestTimeout: TimeSpan.FromSeconds(1),
                workflowTimeout: TimeSpan.FromSeconds(1),
                pollInterval: TimeSpan.FromMilliseconds(1)),
            new StaticCredentialSource("proof-password"));

        var result = await probe.RunAsync("https://runtime.example.test", Environment);

        Assert.True(result.Succeeded);
        Assert.Equal("proof-instance_01", result.WorkflowId);
        Assert.Equal("Finished", result.Result);
        Assert.Equal("Finished", result.SafeMetadata["status"]);
        Assert.Equal("0", result.SafeMetadata["incidentCount"]);
        Assert.Equal("2026-08-31T12:00:00.0000000+00:00", result.SafeMetadata["finishedAt"]);
        Assert.DoesNotContain("bearer-secret", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("proof-password", JsonSerializer.Serialize(result), StringComparison.Ordinal);

        Assert.Equal(9, handler.Requests.Count);
        Assert.Equal((HttpMethod.Get, "/alive"), (handler.Requests[0].Method, handler.Requests[0].Path));
        Assert.Equal((HttpMethod.Get, "/health"), (handler.Requests[1].Method, handler.Requests[1].Path));
        Assert.Equal((HttpMethod.Post, "/elsa/api/identity/login"), (handler.Requests[2].Method, handler.Requests[2].Path));
        Assert.Equal((HttpMethod.Post, "/elsa/api/workflow-definitions"), (handler.Requests[3].Method, handler.Requests[3].Path));
        Assert.Equal((HttpMethod.Post, "/elsa/api/workflow-definitions/elsa-control-disposable-proof/publish"), (handler.Requests[4].Method, handler.Requests[4].Path));
        Assert.Equal((HttpMethod.Get, "/elsa/api/workflow-definitions/by-definition-id/elsa-control-disposable-proof?versionOptions=Published"), (handler.Requests[5].Method, handler.Requests[5].Path));
        Assert.Equal((HttpMethod.Post, "/elsa/api/workflow-definitions/elsa-control-disposable-proof/execute"), (handler.Requests[6].Method, handler.Requests[6].Path));
        Assert.Equal((HttpMethod.Get, "/elsa/api/workflow-instances/proof-instance_01"), (handler.Requests[7].Method, handler.Requests[7].Path));
        Assert.Equal((HttpMethod.Get, "/elsa/api/workflow-instances/proof-instance_01"), (handler.Requests[8].Method, handler.Requests[8].Path));
        Assert.DoesNotContain(handler.Requests[0].Headers, header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Bearer bearer-secret", handler.Requests[5].Headers["Authorization"]);

        using var createBody = JsonDocument.Parse(handler.Requests[3].Body);
        Assert.Equal("elsa-control-disposable-proof", createBody.RootElement.GetProperty("model").GetProperty("definitionId").GetString());
        Assert.False(createBody.RootElement.GetProperty("publish").GetBoolean());
    }

    [Fact]
    public async Task Missing_instance_header_fails_with_stable_code_without_response_body()
    {
        var handler = new RecordingHandler(
            _ =>
            [
                Json(HttpStatusCode.OK), Json(HttpStatusCode.OK),
                Json(HttpStatusCode.OK, "{\"isAuthenticated\":true,\"accessToken\":\"token\"}"),
                Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\"}}"),
                Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}}"),
                Json(HttpStatusCode.OK, "{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}"),
                Json(HttpStatusCode.OK, "{\"error\":\"secret-response-body\"}")
            ]);
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://runtime.example.test", Environment));

        Assert.Equal(DeploymentProofStage.Workflow, exception.Stage);
        Assert.Equal("azure.proof.workflow.instanceHeaderMissing", exception.Code);
        Assert.DoesNotContain("secret-response-body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finished_instance_with_incidents_is_safe_failed_result()
    {
        var handler = SuccessfulSetupHandler(
            Json(HttpStatusCode.OK, "{\"status\":\"Finished\",\"incidentCount\":1,\"finishedAt\":\"2026-08-31T12:00:00Z\"}"));
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var result = await probe.RunAsync("https://runtime.example.test", Environment);

        Assert.False(result.Succeeded);
        Assert.Equal("FinishedWithIncidents", result.Result);
        Assert.Equal("1", result.SafeMetadata["incidentCount"]);
    }

    [Fact]
    public async Task Running_instance_is_bounded_by_workflow_timeout()
    {
        var handler = RunningSetupHandler();
        using var client = new HttpClient(handler);
        var probe = new ElsaHttpWorkflowProbe(
            client,
            new ElsaHttpWorkflowProbeOptions(
                "proof-user",
                requestTimeout: TimeSpan.FromSeconds(1),
                workflowTimeout: TimeSpan.FromMilliseconds(40),
                pollInterval: TimeSpan.FromMilliseconds(5)),
            new StaticCredentialSource("proof-password"));

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://runtime.example.test", Environment));

        Assert.Equal("azure.proof.workflow.timeout", exception.Code);
    }

    [Fact]
    public async Task Endpoint_must_be_verified_https_without_userinfo_or_query()
    {
        var handler = new RecordingHandler(_ => []);
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://user:password@runtime.example.test?token=secret", Environment));

        Assert.Equal("azure.proof.workflow.endpointInvalid", exception.Code);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rejects_redirect_without_following_it()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location = new Uri("https://foreign.example.test/steal");
        var handler = new RecordingHandler(_ => [Json(HttpStatusCode.OK), Json(HttpStatusCode.OK), redirect]);
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://runtime.example.test", Environment));

        Assert.Equal("azure.proof.workflow.redirectRejected", exception.Code);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("runtime.example.test", request.Host));
    }

    [Fact]
    public async Task Rejects_oversized_json_response()
    {
        var handler = new RecordingHandler(_ =>
        [
            Json(HttpStatusCode.OK), Json(HttpStatusCode.OK),
            Json(HttpStatusCode.OK, "{\"padding\":\"" + new string('x', 70_000) + "\"}")
        ]);
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://runtime.example.test", Environment));

        Assert.Equal("azure.proof.workflow.responseTooLarge", exception.Code);
    }

    [Fact]
    public async Task Rejects_endpoint_with_path_prefix()
    {
        var handler = new RecordingHandler(_ => []);
        using var client = new HttpClient(handler);
        var probe = CreateProbe(client);

        var exception = await Assert.ThrowsAsync<DeploymentProofStageException>(() =>
            probe.RunAsync("https://runtime.example.test/prefix", Environment));

        Assert.Equal("azure.proof.workflow.endpointInvalid", exception.Code);
        Assert.Empty(handler.Requests);
    }

    private static ElsaHttpWorkflowProbe CreateProbe(HttpClient client) => new(
        client,
        new ElsaHttpWorkflowProbeOptions(
            "proof-user",
            requestTimeout: TimeSpan.FromSeconds(1),
            workflowTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(1)),
        new StaticCredentialSource("proof-password"));

    private static RecordingHandler SuccessfulSetupHandler(HttpResponseMessage finalPoll) =>
        new(_ =>
        [
            Json(HttpStatusCode.OK), Json(HttpStatusCode.OK),
            Json(HttpStatusCode.OK, "{\"isAuthenticated\":true,\"accessToken\":\"token\"}"),
            Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\"}}"),
            Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}}"),
            Json(HttpStatusCode.OK, "{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}"),
            Json(HttpStatusCode.OK, "{}", new Dictionary<string, string> { ["x-elsa-workflow-instance-id"] = "proof-instance" }),
            finalPoll
        ]);

    private static RecordingHandler RunningSetupHandler()
    {
        var responses = new List<HttpResponseMessage>
        {
            Json(HttpStatusCode.OK),
            Json(HttpStatusCode.OK),
            Json(HttpStatusCode.OK, "{\"isAuthenticated\":true,\"accessToken\":\"token\"}"),
            Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\"}}"),
            Json(HttpStatusCode.OK, "{\"workflowDefinition\":{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}}"),
            Json(HttpStatusCode.OK, "{\"definitionId\":\"elsa-control-disposable-proof\",\"isPublished\":true}"),
            Json(HttpStatusCode.OK, "{}", new Dictionary<string, string> { ["x-elsa-workflow-instance-id"] = "proof-instance" })
        };
        for (var i = 0; i < 32; i++)
            responses.Add(Json(HttpStatusCode.OK, "{\"status\":\"Running\",\"incidentCount\":0}"));

        return new(_ => responses);
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body = "{}",
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        };
        if (headers is not null)
        {
            foreach (var header in headers)
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, IReadOnlyList<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        private bool _initialized;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_initialized)
            {
                foreach (var response in responseFactory(request))
                    _responses.Enqueue(response);
                _initialized = true;
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.RequestUri.Host,
                body,
                request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase)));
            return _responses.Count == 0 ? Json(HttpStatusCode.InternalServerError) : _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Host,
        string Body,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class StaticCredentialSource(string password) : IElsaProofCredentialSource
    {
        public ValueTask<AzureSecretLease> ResolvePasswordAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new AzureSecretLease(password));
        }
    }
}
